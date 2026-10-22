using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkNest.Application.DTOs.Complaint;
using WorkNest.Application.DTOs.WhatsApp;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// The WorkNest WhatsApp bot (port of the former Python chatbot). Main menu:
    /// 1. Book a Tour (branch -> workspace type -> seats -> name -> phone, saved as a Tour Inquiry),
    /// 2. Complaints / Support (branch -> category -> description, saved as a complaint with its branch),
    /// 3. Location & Directions (map pin + address). "0" / "menu" returns to the menu from anywhere.
    /// Branches come from dbo.WN_Locations (active, by Id), read with a short time limit and kept for a few minutes
    /// (<see cref="WhatsAppBranchCache"/>); when it can't be read: the last good list, else WhatsApp:Branches, else the
    /// built-in I-8 / F-7. Titles / map pins / phones: WhatsApp:Branches, else WN_Locations, else the built-in values.
    /// </summary>
    public class WhatsAppBotService : IWhatsAppBotService
    {
        private readonly IWhatsAppRepository _repo;
        private readonly IWhatsAppClient _client;
        private readonly IDbRepository _db;
        private readonly IContactService _contacts;
        private readonly IComplaintService _complaints;
        private readonly IConfiguration _config;
        private readonly WhatsAppBranchCache _branchCache;
        private readonly ILogger<WhatsAppBotService> _logger;

        private static readonly string[] WorkspaceTypes = { "Shared Seat", "Private Office", "Meeting Room" };
        private static readonly string[] ComplaintCategories =
            { "Internet / WiFi", "Electricity / AC", "Cleaning", "Noise", "Access / Entry", "Booking Issue", "Other" };

        // Last resort for the two branches when neither WN_Locations nor WhatsApp:Branches has them (same values the
        // Python bot used); the ids are their dbo.WN_Locations ids, used for ordering only.
        private static readonly WhatsAppBranchOptions[] DefaultBranchOptions =
        {
            new() { Id = 1, Name = "I-8", Title = "I-8 Markaz Branch", Latitude = 33.6684509m, Longitude = 73.0737427m, Phone = "+92 51 8444001",
                    MapsUrl = "https://www.google.com/maps/place/WorkNest+Co+Working/data=!4m2!3m1!1s0x0:0xcb86c3882189a887" },
            new() { Id = 2, Name = "F-7", Title = "F-7 Markaz Branch", Latitude = 33.7215m, Longitude = 73.0558m, Phone = "+92 51 8444002",
                    MapsUrl = "https://www.google.com/maps/place/WorkNest+Co+Working/data=!4m2!3m1!1s0x0:0xf35c1de8139d7bc5" },
        };

        // Last resort for the main menu's sales line when neither WhatsApp:SalesContacts nor WN_Locations has a number.
        private static readonly (string Label, string Phone)[] DefaultSalesContacts =
            { ("F-7, Islamabad", "03280256000"), ("I-8, Islamabad", "03201809696") };

        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        /// <summary>A message after this many quiet minutes starts a new conversation: a half-finished menu (tour, complaint,
        /// directions) is forgotten and the customer gets the welcome menu, never "invalid option".</summary>
        private const int NewSessionAfterMinutes = 30;

        /// <summary>Steps where the customer types free text (a name, a complaint): a "Salam, ..." there is the answer.</summary>
        private static readonly HashSet<string> FreeTextStates = new(StringComparer.Ordinal)
            { "main_menu", "booking_name", "booking_phone", "complaint_description" };

        private static readonly HashSet<string> Greetings = new(StringComparer.Ordinal)
        {
            "hi", "hy", "helo", "hey", "heya", "hai", "hiya", "yo", "start", "menu",
            "salam", "salaam", "aoa", "asalamualaikum", "asalamoalaikum", "asalamualikum", "aslamualaikum", "aslamoalaikum",
            "goodmorning", "godmorning", "goodafternoon", "godafternoon", "goodevening", "godevening", "gm"
        };

        /// <summary>"Hellllooooo", "hiii!!", "Assalam o Alaikum", "Good morning" … all count as a greeting.</summary>
        private static bool IsGreeting(string lower)
        {
            var letters = new string(lower.Where(char.IsLetter).ToArray());
            if (letters.Length == 0) return false;
            var squeezed = Regex.Replace(letters, @"(.)\1+", "$1");   // hellloooo -> helo, hiiii -> hi
            return Greetings.Contains(letters) || Greetings.Contains(squeezed)
                   || squeezed.StartsWith("asalam") || squeezed.StartsWith("aslam") || squeezed.StartsWith("salam")
                   || (squeezed.StartsWith("helo") && squeezed.Length <= 8) || (squeezed.StartsWith("hey") && squeezed.Length <= 6);
        }

        public WhatsAppBotService(IWhatsAppRepository repo, IWhatsAppClient client, IDbRepository db, IContactService contacts,
            IComplaintService complaints, IConfiguration config, WhatsAppBranchCache branchCache, ILogger<WhatsAppBotService> logger)
        {
            _repo = repo;
            _client = client;
            _db = db;
            _contacts = contacts;
            _complaints = complaints;
            _config = config;
            _branchCache = branchCache;
            _logger = logger;
        }

        // ---- state ------------------------------------------------------------------------------

        private sealed class BotData
        {
            public string? Branch { get; set; }
            /// <summary>The branch's dbo.WN_Locations.Id (null in conversations saved before it was kept, or when the
            /// branch came from a fallback list, not WN_Locations): the tour inquiry is saved with it (BR_01 branch routing).</summary>
            public int? BranchId { get; set; }
            public string? WorkspaceType { get; set; }
            public string? Seats { get; set; }
            public string? Name { get; set; }
            public string? ComplaintCategory { get; set; }
        }

        private sealed class Turn
        {
            public string State = "main_menu";
            public BotData Data = new();
            public readonly List<string> Texts = new();
            public readonly List<(Branch Branch, WhatsAppBranchOptions Options)> Pins = new();
            /// <summary>First message from this customer, or the first after a quiet spell: greet, never "invalid option".</summary>
            public bool NewSession;
            public void Reset(string state = "main_menu") { State = state; Data = new BotData(); }
        }

        /// <summary>
        /// A branch to choose. <see cref="IsLocation"/>: <see cref="Id"/> is a WN_Locations id (the database or its last
        /// good list); false for the WhatsApp:Branches / built-in fallbacks, whose id is never saved as a location.
        /// Latitude / Longitude / Phone: WN_Locations' own values, when its rows carry them.
        /// </summary>
        private sealed record Branch(int Id, string Name, string? Address, bool IsLocation,
            decimal? Latitude = null, decimal? Longitude = null, string? Phone = null, string? City = null);

        // ---- entry ------------------------------------------------------------------------------

        public async Task HandleIncomingAsync(string phone, string? profileName, string? whatsAppMessageId, string messageType, string? text)
        {
            var (_, conversationId) = await _repo.EnsureConversationAsync(phone, profileName);
            var incomingText = messageType == "text" ? (text ?? "") : $"[{messageType} message]";
            if (!await _repo.AddMessageAsync(conversationId, "incoming", messageType, incomingText, whatsAppMessageId, null))
                return; // the same message delivered again by Meta: already answered

            var saved = await _repo.GetBotStateAsync(phone);
            var turn = new Turn
            {
                State = saved?.State ?? "main_menu",
                NewSession = saved == null || saved.Value.MinutesSinceUpdate >= NewSessionAfterMinutes
            };
            if (!string.IsNullOrWhiteSpace(saved?.Data))
            {
                try { turn.Data = JsonSerializer.Deserialize<BotData>(saved.Value.Data!, Json) ?? new BotData(); } catch { turn.Data = new BotData(); }
            }
            // The conversation expired: start again from the main menu instead of resuming the old step.
            if (turn.NewSession) turn.Reset();

            var branches = await GetBranchesAsync();
            if (messageType != "text")
                turn.Texts.Add("Currently, I can process text messages only.\n\nPlease send a text message.\n\n" + MainMenu(branches));
            else
            {
                var stateBefore = turn.State;
                try
                {
                    await ReplyAsync(turn, phone, profileName, (text ?? "").Trim(), branches);
                }
                catch (Exception ex)
                {
                    // Never leave the customer without an answer: a failure on our side (e.g. the database) used to stop
                    // the reply, so the customer saw nothing and the same step failed again. They stay on the same step
                    // (their answers are kept) and get the sales numbers.
                    _logger.LogError(ex, "WhatsApp bot: the reply to {Phone} at step {State} failed", phone, stateBefore);
                    turn.State = stateBefore;
                    turn.Texts.Clear();
                    turn.Pins.Clear();
                    turn.Texts.Add(SomethingWentWrong(branches));
                }
            }

            await _repo.SaveBotStateAsync(phone, turn.State, JsonSerializer.Serialize(turn.Data, Json));

            // Map pins first, then the text (as the Python bot did).
            foreach (var (branch, opts) in turn.Pins)
            {
                if (opts.Latitude is null || opts.Longitude is null) continue;
                var pin = await _client.SendLocationAsync(phone, opts.Latitude.Value, opts.Longitude.Value,
                    $"WorkNest Co-Working - {opts.Title ?? branch.Name}", branch.Address ?? "");
                if (pin.Ok) await _repo.AddMessageAsync(conversationId, "outgoing", "location", $"[Location: {branch.Name}]", pin.MessageId, null);
            }
            foreach (var reply in turn.Texts)
            {
                var res = await _client.SendTextAsync(phone, reply);
                if (res.Ok) await _repo.AddMessageAsync(conversationId, "outgoing", "text", reply, res.MessageId, null);
                else _logger.LogWarning("WhatsApp reply to {Phone} failed: {Error}", phone, res.Error);
            }
        }

        // ---- conversation -----------------------------------------------------------------------

        private async Task ReplyAsync(Turn t, string phone, string? profileName, string message, List<Branch> branches)
        {
            var lower = message.ToLowerInvariant();

            // Anywhere: back to the menu.
            if (lower is "menu" or "home" or "start" or "0")
            {
                t.Reset();
                t.Texts.Add(MainMenu(branches));
                return;
            }

            // "Hi" while the bot waits for a menu number starts over, instead of "Please select a valid category".
            if (!FreeTextStates.Contains(t.State) && IsGreeting(lower))
            {
                t.Reset();
                t.Texts.Add("Hello!\n\n" + MainMenu(branches));
                return;
            }

            // Anywhere: "<branch> location" / "<branch> pin" sends that branch's pin.
            foreach (var b in branches)
            {
                var key = Norm(b.Name);
                if (key.Length > 0 && (Norm(lower).Contains(key + "location") || Norm(lower).Contains(key + "pin")))
                {
                    t.Reset();
                    AddBranchDirections(t, b);
                    return;
                }
            }

            // Anywhere: asking for directions.
            if (lower is "location" or "directions" or "address" or "map" or "pin" or "where are you"
                || lower.Contains("where is worknest") || lower.Contains("how to reach"))
            {
                t.Reset("location_branch");
                t.Texts.Add(LocationPrompt(branches));
                return;
            }

            // Customer answering "is your complaint resolved? 1 / 2".
            if (t.State == "main_menu" && message is "1" or "2")
            {
                var result = await _complaints.HandleResolutionReplyAsync(new ChatbotResolutionReplyRequest
                {
                    Phone = phone,
                    Response = message == "1" ? "persists" : "resolved"
                });
                JsonElement? data = result.Data is null ? null : JsonSerializer.SerializeToElement(result.Data, Json);
                if (result.IsSuccessful && data is { } d && d.TryGetProperty("handled", out var h) && h.ValueKind == JsonValueKind.True)
                {
                    t.Reset();
                    var oldNo = d.TryGetProperty("complaintNo", out var o) ? o.GetString() : null;
                    if (message == "2")
                    {
                        t.Texts.Add($"Thank you for confirming.\n\nComplaint {oldNo} has been closed.\n\nIf you need anything else, please type 0.");
                    }
                    else
                    {
                        var newNo = d.TryGetProperty("newComplaintNo", out var n) ? n.GetString() : null;
                        t.Texts.Add("We're sorry the issue is still persisting.\n\n" +
                                    "A new complaint has been submitted for further review.\n\n" +
                                    $"Previous Complaint ID: {oldNo}\n" +
                                    (newNo != null ? $"New Complaint ID: {newNo}\n\n" : "\n") +
                                    "Status: Open\n\nOur team will investigate the issue again and contact you if further information is required.\n\n" +
                                    "Type 0 to return to the main menu.");
                    }
                    return;
                }
            }

            switch (t.State)
            {
                case "booking_branch":
                {
                    var b = MatchBranch(branches, lower);
                    if (b == null) { t.Texts.Add("Please select a valid branch:\n\n" + BranchList(branches) + "\nType 0 for the main menu.\n\nReply with a number."); return; }
                    t.Data.Branch = b.Name;
                    t.Data.BranchId = b.IsLocation ? b.Id : null;
                    t.State = "booking_type";
                    t.Texts.Add($"Branch: {b.Name}\n\nPlease select a workspace type:\n\n{NumberedList(WorkspaceTypes)}\nType 0 for the main menu.\n\nReply with a number.");
                    return;
                }
                case "booking_type":
                {
                    if (!int.TryParse(message, out var i) || i < 1 || i > WorkspaceTypes.Length)
                    {
                        t.Texts.Add($"Please select a valid workspace type:\n\n{NumberedList(WorkspaceTypes)}\nType 0 for the main menu.\n\nReply with a number.");
                        return;
                    }
                    t.Data.WorkspaceType = WorkspaceTypes[i - 1];
                    t.State = "booking_seats";
                    t.Texts.Add($"Branch: {t.Data.Branch}\nWorkspace: {t.Data.WorkspaceType}\n\nPlease enter the number of seats / persons required.\n\nExample: 1 (or 100)\n\nType 0 for the main menu.");
                    return;
                }
                case "booking_seats":
                {
                    var digits = Regex.Replace(message, @"[^\d]", "");
                    if (!int.TryParse(digits, out var seats) || seats <= 0 || seats > 500)
                    {
                        t.Texts.Add("Please enter a valid number of seats / persons required.\n\nExample: 1 (or 100)\n\nType 0 for the main menu.");
                        return;
                    }
                    t.Data.Seats = seats.ToString();
                    t.State = "booking_name";
                    t.Texts.Add($"Seats: {seats}\n\nPlease enter your Full Name.\n\nExample: Luqman Ahmad\n\nType 0 for the main menu.");
                    return;
                }
                case "booking_name":
                {
                    if (message.Length < 2 || message.Length > 200) { t.Texts.Add("Please enter a valid full name.\n\nType 0 for the main menu."); return; }
                    t.Data.Name = message;
                    // The phone number is the customer's WhatsApp number: no need to ask for it.
                    await FinishTourAsync(t, phone, profileName, branches);
                    return;
                }
                case "booking_phone": // conversations started before the phone question was removed
                {
                    await FinishTourAsync(t, phone, profileName, branches);
                    return;
                }
                case "complaint_branch":
                {
                    var b = MatchBranch(branches, lower);
                    if (b == null) { t.Texts.Add("Please select a valid branch:\n\n" + BranchList(branches) + "\nType 0 for the main menu.\n\nReply with a number."); return; }
                    t.Data.Branch = b.Name;
                    t.State = "complaint_category";
                    t.Texts.Add($"Branch: {b.Name}\n\nPlease select a category:\n\n{NumberedList(ComplaintCategories)}\nType 0 for the main menu.\n\nReply with a number.");
                    return;
                }
                case "complaint_category":
                {
                    if (!int.TryParse(message, out var i) || i < 1 || i > ComplaintCategories.Length)
                    {
                        t.Texts.Add($"Please select a valid category:\n\n{NumberedList(ComplaintCategories)}\nType 0 for the main menu.");
                        return;
                    }
                    t.Data.ComplaintCategory = ComplaintCategories[i - 1];
                    t.State = "complaint_description";
                    t.Texts.Add($"{BranchLine(t.Data.Branch)}Category: {t.Data.ComplaintCategory}\n\nPlease describe your issue.");
                    return;
                }
                case "complaint_description":
                {
                    var category = t.Data.ComplaintCategory ?? "Other";
                    var branch = t.Data.Branch;
                    var result = await _complaints.CreateFromChatbotAsync(new ChatbotComplaintRequest
                    {
                        Phone = phone,
                        Name = profileName,
                        Branch = branch,
                        Category = category,
                        Description = message
                    });
                    var data = result.Data is null ? (JsonElement?)null : JsonSerializer.SerializeToElement(result.Data, Json);
                    var no = data is { } d && d.TryGetProperty("complaintNo", out var c) ? c.GetString() : null;
                    if (!result.IsSuccessful || no == null)
                    {
                        t.Texts.Add(result.IsSuccessful ? "We were unable to submit your complaint right now.\n\nPlease try again."
                                                        : (result.Message ?? "We were unable to submit your complaint right now.") + "\n\nPlease try again.");
                        return;
                    }
                    t.Reset();
                    t.Texts.Add("Your complaint has been submitted.\n\n" +
                                $"Complaint ID: {no}\n{BranchLine(branch)}Category: {category}\nStatus: Open\n\n" +
                                "Our team will review your complaint and contact you if further information is required.\n\n" +
                                "Type 0 to return to the main menu.");
                    return;
                }
                case "location_branch":
                {
                    var b = MatchBranch(branches, lower);
                    if (b == null) { t.Texts.Add("Please select a valid branch for directions:\n\n" + BranchList(branches) + "\nType 0 for the main menu.\n\nReply with a number."); return; }
                    t.Reset();
                    AddBranchDirections(t, b);
                    return;
                }
            }

            // Main menu (and anything unexpected).
            t.Reset();
            if (IsGreeting(lower))
            {
                t.Texts.Add("Hello!\n\n" + MainMenu(branches));
            }
            else if (message == "1" || lower is "book" or "booking" or "book a tour" or "tour")
            {
                t.State = "booking_branch";
                t.Texts.Add("Book a Tour\n\nPlease select a branch:\n\n" + BranchList(branches) + "\nType 0 for the main menu.\n\nReply with a number.");
            }
            else if (message == "2" || lower is "complaint" or "complaints" or "support")
            {
                if (branches.Count > 1)
                {
                    // Which branch first (1. I-8, 2. F-7 from dbo.WN_Locations), then the category.
                    t.State = "complaint_branch";
                    t.Texts.Add("Complaints / Support\n\nPlease select your branch:\n\n" + BranchList(branches) + "\nType 0 for the main menu.\n\nReply with a number.");
                }
                else
                {
                    // A single branch: nothing to ask.
                    t.Data.Branch = branches.FirstOrDefault()?.Name;
                    t.State = "complaint_category";
                    t.Texts.Add($"Complaints / Support\n\n{BranchLine(t.Data.Branch)}Please select a category:\n\n{NumberedList(ComplaintCategories)}\nType 0 for the main menu.\n\nReply with a number.");
                }
            }
            else if (message == "3")
            {
                t.State = "location_branch";
                t.Texts.Add(LocationPrompt(branches));
            }
            else if (lower is "reception" or "talk to reception" or "contact" or "phone")
            {
                t.Texts.Add(ReceptionText(branches));
            }
            else if (t.NewSession)
            {
                // First message (or first after a while) that isn't a menu choice: just welcome them.
                t.Texts.Add(MainMenu(branches));
            }
            else
            {
                t.Texts.Add("Please select a valid option.\n\n" + MainMenu(branches));
            }
        }

        /// <summary>Saves the Book a Tour answers as a Tour Inquiry, with the customer's WhatsApp number as the phone.
        /// LocationId only when WN_Locations confirmed the branch; a fallback branch is saved by name ("Branch: X"),
        /// which the inquiry matches to an active location (BR_01) or the staff assign.</summary>
        private async Task FinishTourAsync(Turn t, string whatsAppPhone, string? profileName, List<Branch> branches)
        {
            var phone = LocalPhone(whatsAppPhone);
            var reference = "WN-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            var d = t.Data;
            ApiResponse saved;
            try
            {
                saved = await _contacts.CreateChatbotTourInquiryAsync(new ChatbotTourInquiryRequest
                {
                    Name = d.Name ?? profileName ?? "WhatsApp customer",
                    Phone = phone,
                    Branch = d.Branch,
                    LocationId = d.BranchId,
                    WorkspaceType = d.WorkspaceType,
                    Seats = int.TryParse(d.Seats, out var s) ? s : null,
                    Reference = reference
                });
            }
            catch (Exception ex)
            {
                // The save failed on our side: the customer is told and can retry (below), and the error is logged.
                _logger.LogError(ex, "WhatsApp bot: saving the tour request of {Phone} failed", whatsAppPhone);
                saved = ApiResponse.Fail("The tour request could not be saved.");
            }
            if (!saved.IsSuccessful)
            {
                t.State = "booking_phone"; // any reply retries the save with the same answers
                t.Texts.Add("We could not save your tour request right now. Please reply with any message to try again, or call our sales team.\n\n" + SalesContacts(branches));
                return;
            }
            t.Texts.Add("Tour request received!\n\n" +
                        $"Reference: {reference}\n\n" +
                        $"Branch: {d.Branch}\nWorkspace: {d.WorkspaceType}\nSeats / Persons: {d.Seats}\nName: {d.Name}\nPhone: {phone}\n\n" +
                        "Our sales team will contact you on this WhatsApp number shortly.\n\nThank you for choosing WorkNest!\n\nType 0 to return to the main menu.");
            t.Reset();
        }

        /// <summary>The answer when a step fails on our side: try again, or call sales.</summary>
        private string SomethingWentWrong(List<Branch> branches) =>
            "Sorry, something went wrong on our side. Please send your last reply again in a moment, or contact our sales team directly.\n\n" +
            SalesContacts(branches);

        /// <summary>WhatsApp number (923001234567) shown the local way (03001234567); other countries keep +.</summary>
        private static string LocalPhone(string whatsAppPhone)
        {
            var digits = new string((whatsAppPhone ?? "").Where(char.IsDigit).ToArray());
            if (digits.StartsWith("92") && digits.Length == 12) return "0" + digits[2..];
            return digits.Length > 0 ? "+" + digits : whatsAppPhone ?? "";
        }

        // ---- texts ------------------------------------------------------------------------------

        private string MainMenu(List<Branch> branches) =>
            "Welcome to WorkNest Co-Working!\n\nHow can we help you today?\n\n" +
            "1. Book a Tour\n2. Complaints / Support\n3. Location & Directions\n\n" +
            "Reply with a number.\n\n" + SalesContacts(branches);

        /// <summary>WhatsApp:SalesContacts ([{ "Label": "F-7, Islamabad", "Phone": "..." }]), else the phones WN_Locations
        /// gives its branches, else the built-in sales numbers.</summary>
        private string SalesContacts(List<Branch> branches)
        {
            var contacts = _config.GetSection("WhatsApp:SalesContacts").GetChildren()
                .Select(c => (Label: c["Label"] ?? "", Phone: c["Phone"] ?? ""))
                .Where(c => !string.IsNullOrWhiteSpace(c.Label) && !string.IsNullOrWhiteSpace(c.Phone)).ToList();
            if (contacts.Count == 0)
                contacts = branches.Where(b => b.IsLocation && !string.IsNullOrWhiteSpace(b.Phone))
                    .Select(b => (Label: string.IsNullOrWhiteSpace(b.City) ? b.Name : $"{b.Name}, {b.City}", Phone: b.Phone!)).ToList();
            if (contacts.Count == 0)
                contacts = DefaultSalesContacts.ToList();
            return "For direct contact to sales\n" + string.Join("\n", contacts.Select(c => $"{c.Label}: {c.Phone}"));
        }

        private string LocationPrompt(List<Branch> branches) =>
            "WorkNest Locations & Directions\n\nPlease select a branch to receive the live interactive map pin:\n\n" +
            string.Join("\n\n", branches.Select((b, i) => $"{i + 1}. {Options(b).Title ?? b.Name}" + (string.IsNullOrWhiteSpace(b.Address) ? "" : $"\nAddress: {b.Address}"))) +
            "\n\nType 0 for the main menu.\n\nReply with a number.";

        private string ReceptionText(List<Branch> branches) =>
            "WorkNest Reception & Support\n\nOur front desk teams are available Saturday to Thursday (9:00 AM - 9:00 PM):\n\n" +
            string.Join("\n\n", branches.Select((b, i) =>
            {
                var o = Options(b);
                return $"{i + 1}. {o.Title ?? b.Name}:\n" +
                       (string.IsNullOrWhiteSpace(b.Address) ? "" : $"Address: {b.Address}\n") +
                       (string.IsNullOrWhiteSpace(o.MapsUrl) ? "" : $"Google Maps: {o.MapsUrl}\n") +
                       (string.IsNullOrWhiteSpace(o.Phone) ? "" : $"Phone / WhatsApp: {o.Phone}");
            })) +
            "\n\nType 0 to return to the main menu.";

        private void AddBranchDirections(Turn t, Branch b)
        {
            var o = Options(b);
            t.Pins.Add((b, o));
            t.Texts.Add($"WorkNest {o.Title ?? b.Name}\n\n" +
                        (string.IsNullOrWhiteSpace(b.Address) ? "" : $"Address: {b.Address}\n") +
                        (string.IsNullOrWhiteSpace(o.MapsUrl) ? "" : $"Google Maps: {o.MapsUrl}\n") +
                        (string.IsNullOrWhiteSpace(o.Phone) ? "" : $"Phone: {o.Phone}\n") +
                        "Hours: Open 24/7 for registered members. Front reception: 9:00 AM - 9:00 PM.\n\n" +
                        (o.Latitude != null ? "An interactive WhatsApp map pin has been sent directly below. Tap the pin or link to navigate.\n\n" : "") +
                        "Type 0 to return to the main menu.");
        }

        private static string NumberedList(IEnumerable<string> items) => string.Concat(items.Select((x, i) => $"{i + 1}. {x}\n"));
        private static string BranchList(List<Branch> branches) => NumberedList(branches.Select(b => b.Name));
        /// <summary>"Branch: I-8" + newline, or nothing for conversations started before the branch question.</summary>
        private static string BranchLine(string? branch) => string.IsNullOrWhiteSpace(branch) ? "" : $"Branch: {branch}\n";

        // ---- branches ---------------------------------------------------------------------------

        /// <summary>
        /// Active locations from dbo.WN_Locations in Id order (I-8 = 1, F-7 = 2), so "1. I-8 / 2. F-7". Read at most every
        /// <see cref="WhatsAppBranchCache.Lifetime"/>, and a reply waits at most <see cref="WhatsAppBranchCache.LoadTimeout"/>
        /// for it. When it fails, is slow or has no active location: the last good list, else WhatsApp:Branches, else the
        /// built-in I-8 / F-7 (a warning names the fallback used).
        /// </summary>
        private async Task<List<Branch>> GetBranchesAsync()
        {
            if (_branchCache.IsFresh && _branchCache.LastGood is { } fresh) return FromLocationRows(fresh);

            string problem;
            Exception? error = null;
            try
            {
                var load = _db.GetAllLocationsAsync();
                using var stopWaiting = new CancellationTokenSource();
                var timeLimit = Task.Delay(_branchCache.LoadTimeout, stopWaiting.Token);
                if (await Task.WhenAny(load, timeLimit) == load)
                {
                    stopWaiting.Cancel();
                    var rows = UsableRows(await load);
                    if (rows.Count > 0)
                    {
                        _branchCache.Store(rows);
                        return FromLocationRows(rows);
                    }
                    problem = "WN_Locations has no active location";
                }
                else
                {
                    problem = $"WN_Locations did not answer within {_branchCache.LoadTimeout.TotalSeconds:0.#} s";
                    _ = StoreWhenLoadedAsync(load, _branchCache, _logger);   // a late answer still serves the next messages
                }
            }
            catch (Exception ex)
            {
                problem = "could not load WN_Locations";
                error = ex;
            }

            if (_branchCache.LastGood is { } last)
            {
                _logger.LogWarning(error, "WhatsApp bot: {Problem}; using the last good branch list (loaded {LoadedAtUtc:u})",
                    problem, _branchCache.LoadedAtUtc);
                return FromLocationRows(last);
            }
            var configured = ConfiguredBranchOptions().Where(o => o.Name.Length > 0).ToList();
            if (configured.Count > 0)
            {
                _logger.LogWarning(error, "WhatsApp bot: {Problem}; using the WhatsApp:Branches configuration", problem);
                return FallbackBranches(configured);
            }
            _logger.LogWarning(error, "WhatsApp bot: {Problem}; using the built-in branches (I-8, F-7)", problem);
            return FallbackBranches(DefaultBranchOptions);
        }

        private static async Task StoreWhenLoadedAsync(Task<IEnumerable<IDictionary<string, object?>>> load,
            WhatsAppBranchCache cache, ILogger logger)
        {
            try
            {
                var rows = UsableRows(await load);
                if (rows.Count > 0) cache.Store(rows);
            }
            catch (Exception ex) { logger.LogWarning(ex, "WhatsApp bot: the late WN_Locations load failed"); }
        }

        /// <summary>Rows with a name (null-safe).</summary>
        private static List<IDictionary<string, object?>> UsableRows(IEnumerable<IDictionary<string, object?>>? rows) =>
            (rows ?? Enumerable.Empty<IDictionary<string, object?>>()).Where(r => Text(r, "Name") != null).ToList();

        /// <summary>WN_Locations rows (dbo.WN_Locations_GetAll) as branches in Id order. Latitude / Longitude / Phone are
        /// taken when the rows carry them (WN_Locations_GetAll returns none of them today).</summary>
        private static List<Branch> FromLocationRows(IEnumerable<IDictionary<string, object?>> rows) =>
            rows.Select(r => new Branch(
                    r.TryGetValue("Id", out var id) && id is not null and not DBNull ? Convert.ToInt32(id) : 0,
                    Text(r, "Name") ?? "",
                    Text(r, "Address"),
                    IsLocation: true,
                    Latitude: Number(r, "Latitude"),
                    Longitude: Number(r, "Longitude"),
                    Phone: Text(r, "Phone"),
                    City: Text(r, "CityName")))
                .Where(b => b.Name.Length > 0)
                .OrderBy(b => b.Id)
                .ToList();

        /// <summary>A fallback list (WhatsApp:Branches or the built-in one): ordered by its configured ids, never saved as
        /// a location id (WN_Locations did not confirm it); the name still goes with the tour inquiry / complaint.</summary>
        private static List<Branch> FallbackBranches(IEnumerable<WhatsAppBranchOptions> options) =>
            options.OrderBy(o => o.Id ?? int.MaxValue)
                .Select(o => new Branch(o.Id ?? 0, o.Name.Trim(), null, IsLocation: false))
                .ToList();

        private static string? Text(IDictionary<string, object?> row, string key) =>
            row.TryGetValue(key, out var v) && v is not null and not DBNull && v.ToString()!.Trim() is { Length: > 0 } s ? s : null;

        private static decimal? Number(IDictionary<string, object?> row, string key)
        {
            if (!row.TryGetValue(key, out var v) || v is null or DBNull) return null;
            try { return Convert.ToDecimal(v, CultureInfo.InvariantCulture); } catch { return null; }
        }

        /// <summary>WhatsApp:Branches: [{ "Id": 1, "Name": "I-8", "Title": "...", "Latitude": .., "Longitude": .., "Phone": "...", "MapsUrl": "..." }].</summary>
        private List<WhatsAppBranchOptions> ConfiguredBranchOptions() =>
            _config.GetSection("WhatsApp:Branches").GetChildren()
                .Select(c => new WhatsAppBranchOptions
                {
                    Id = int.TryParse(c["Id"], out var id) ? id : null,
                    Name = (c["Name"] ?? "").Trim(),
                    Title = c["Title"],
                    Latitude = decimal.TryParse(c["Latitude"], NumberStyles.Float, CultureInfo.InvariantCulture, out var la) ? la : null,
                    Longitude = decimal.TryParse(c["Longitude"], NumberStyles.Float, CultureInfo.InvariantCulture, out var lo) ? lo : null,
                    Phone = c["Phone"],
                    MapsUrl = c["MapsUrl"]
                })
                .ToList();

        /// <summary>What the bot shows for a branch, field by field: WhatsApp:Branches (matched by name), else what
        /// WN_Locations gives, else the built-in values for I-8 / F-7.</summary>
        private WhatsAppBranchOptions Options(Branch b)
        {
            var cfg = ConfiguredBranchOptions().FirstOrDefault(o => Norm(o.Name) == Norm(b.Name));
            var builtIn = DefaultBranchOptions.FirstOrDefault(o => Norm(o.Name) == Norm(b.Name));
            var (lat, lng) = cfg is { Latitude: not null, Longitude: not null } ? (cfg.Latitude, cfg.Longitude)
                : b is { Latitude: not null, Longitude: not null } ? (b.Latitude, b.Longitude)
                : (builtIn?.Latitude, builtIn?.Longitude);
            return new WhatsAppBranchOptions
            {
                Id = b.Id,
                Name = b.Name,
                Title = NonEmpty(cfg?.Title) ?? builtIn?.Title,
                Latitude = lat,
                Longitude = lng,
                Phone = NonEmpty(cfg?.Phone) ?? b.Phone ?? builtIn?.Phone,
                MapsUrl = NonEmpty(cfg?.MapsUrl) ?? builtIn?.MapsUrl
            };
        }

        private static string? NonEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

        private static Branch? MatchBranch(List<Branch> branches, string lower)
        {
            if (int.TryParse(lower, out var i) && i >= 1 && i <= branches.Count) return branches[i - 1];
            var key = Norm(lower).Replace("markaz", "");
            return key.Length == 0 ? null : branches.FirstOrDefault(b => Norm(b.Name) == key);
        }

        /// <summary>Lower-case letters and digits only: "I-8", "i 8", "i8" all become "i8".</summary>
        private static string Norm(string? s) => new string((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    }
}
