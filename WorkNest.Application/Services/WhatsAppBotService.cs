using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkNest.Application.DTOs.Complaint;
using WorkNest.Application.DTOs.WhatsApp;
using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// The WorkNest WhatsApp bot (port of the former Python chatbot). Main menu:
    /// 1. Book a Tour (branch -> workspace type -> seats -> name -> phone, saved as a Tour Inquiry),
    /// 2. Complaints / Support (category -> description, saved as a complaint),
    /// 3. Location & Directions (map pin + address). "0" / "menu" returns to the menu from anywhere.
    /// Branches come from dbo.WN_Locations (active, by Id); map pins / phones from WhatsApp:Branches.
    /// </summary>
    public class WhatsAppBotService : IWhatsAppBotService
    {
        private readonly IWhatsAppRepository _repo;
        private readonly IWhatsAppClient _client;
        private readonly IDbRepository _db;
        private readonly IContactService _contacts;
        private readonly IComplaintService _complaints;
        private readonly IConfiguration _config;
        private readonly ILogger<WhatsAppBotService> _logger;

        private static readonly string[] WorkspaceTypes = { "Shared Seat", "Private Office", "Meeting Room" };
        private static readonly string[] ComplaintCategories =
            { "Internet / WiFi", "Electricity / AC", "Cleaning", "Noise", "Access / Entry", "Booking Issue", "Other" };

        // Defaults for the two branches when WhatsApp:Branches is not configured (same values the Python bot used).
        private static readonly WhatsAppBranchOptions[] DefaultBranchOptions =
        {
            new() { Name = "I-8", Title = "I-8 Markaz Branch", Latitude = 33.6684509m, Longitude = 73.0737427m, Phone = "+92 51 8444001",
                    MapsUrl = "https://www.google.com/maps/place/WorkNest+Co+Working/data=!4m2!3m1!1s0x0:0xcb86c3882189a887" },
            new() { Name = "F-7", Title = "F-7 Markaz Branch", Latitude = 33.7215m, Longitude = 73.0558m, Phone = "+92 51 8444002",
                    MapsUrl = "https://www.google.com/maps/place/WorkNest+Co+Working/data=!4m2!3m1!1s0x0:0xf35c1de8139d7bc5" },
        };

        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        /// <summary>A message after this many quiet minutes starts a new conversation (welcome menu, no "invalid option").</summary>
        private const int NewSessionAfterMinutes = 30;

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
            IComplaintService complaints, IConfiguration config, ILogger<WhatsAppBotService> logger)
        {
            _repo = repo;
            _client = client;
            _db = db;
            _contacts = contacts;
            _complaints = complaints;
            _config = config;
            _logger = logger;
        }

        // ---- state ------------------------------------------------------------------------------

        private sealed class BotData
        {
            public string? Branch { get; set; }
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

        private sealed record Branch(int Id, string Name, string? Address);

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

            if (messageType != "text")
                turn.Texts.Add("Currently, I can process text messages only.\n\nPlease send a text message.\n\n" + MainMenu());
            else
                await ReplyAsync(turn, phone, profileName, (text ?? "").Trim());

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

        private async Task ReplyAsync(Turn t, string phone, string? profileName, string message)
        {
            var lower = message.ToLowerInvariant();
            var branches = await GetBranchesAsync();

            // Anywhere: back to the menu.
            if (lower is "menu" or "home" or "start" or "0")
            {
                t.Reset();
                t.Texts.Add(MainMenu());
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
                    t.Texts.Add($"Branch: {t.Data.Branch}\nWorkspace: {t.Data.WorkspaceType}\n\nPlease enter the number of seats / persons required.\n\nExample: 1 (or 5)\n\nType 0 for the main menu.");
                    return;
                }
                case "booking_seats":
                {
                    var digits = Regex.Replace(message, @"[^\d]", "");
                    if (!int.TryParse(digits, out var seats) || seats <= 0 || seats > 500)
                    {
                        t.Texts.Add("Please enter a valid number of seats / persons required.\n\nExample: 1 (or 5)\n\nType 0 for the main menu.");
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
                    await FinishTourAsync(t, phone, profileName);
                    return;
                }
                case "booking_phone": // conversations started before the phone question was removed
                {
                    await FinishTourAsync(t, phone, profileName);
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
                    t.Texts.Add($"Category: {t.Data.ComplaintCategory}\n\nPlease describe your issue.");
                    return;
                }
                case "complaint_description":
                {
                    var category = t.Data.ComplaintCategory ?? "Other";
                    var result = await _complaints.CreateFromChatbotAsync(new ChatbotComplaintRequest
                    {
                        Phone = phone,
                        Name = profileName,
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
                                $"Complaint ID: {no}\nCategory: {category}\nStatus: Open\n\n" +
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
                t.Texts.Add("Hello!\n\n" + MainMenu());
            }
            else if (message == "1" || lower is "book" or "booking" or "book a tour" or "tour")
            {
                t.State = "booking_branch";
                t.Texts.Add("Book a Tour\n\nPlease select a branch:\n\n" + BranchList(branches) + "\nType 0 for the main menu.\n\nReply with a number.");
            }
            else if (message == "2" || lower is "complaint" or "complaints" or "support")
            {
                t.State = "complaint_category";
                t.Texts.Add($"Complaints / Support\n\nPlease select a category:\n\n{NumberedList(ComplaintCategories)}\nType 0 for the main menu.\n\nReply with a number.");
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
                t.Texts.Add(MainMenu());
            }
            else
            {
                t.Texts.Add("Please select a valid option.\n\n" + MainMenu());
            }
        }

        /// <summary>Saves the Book a Tour answers as a Tour Inquiry, with the customer's WhatsApp number as the phone.</summary>
        private async Task FinishTourAsync(Turn t, string whatsAppPhone, string? profileName)
        {
            var phone = LocalPhone(whatsAppPhone);
            var reference = "WN-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            var d = t.Data;
            var saved = await _contacts.CreateChatbotTourInquiryAsync(new ChatbotTourInquiryRequest
            {
                Name = d.Name ?? profileName ?? "WhatsApp customer",
                Phone = phone,
                Branch = d.Branch,
                WorkspaceType = d.WorkspaceType,
                Seats = int.TryParse(d.Seats, out var s) ? s : null,
                Reference = reference
            });
            if (!saved.IsSuccessful)
            {
                t.State = "booking_phone"; // any reply retries the save with the same answers
                t.Texts.Add("We could not save your tour request right now. Please reply with any message to try again, or call our sales team.\n\n" + SalesContacts());
                return;
            }
            t.Texts.Add("Tour request received!\n\n" +
                        $"Reference: {reference}\n\n" +
                        $"Branch: {d.Branch}\nWorkspace: {d.WorkspaceType}\nSeats / Persons: {d.Seats}\nName: {d.Name}\nPhone: {phone}\n\n" +
                        "Our sales team will contact you on this WhatsApp number shortly.\n\nThank you for choosing WorkNest!\n\nType 0 to return to the main menu.");
            t.Reset();
        }

        /// <summary>WhatsApp number (923001234567) shown the local way (03001234567); other countries keep +.</summary>
        private static string LocalPhone(string whatsAppPhone)
        {
            var digits = new string((whatsAppPhone ?? "").Where(char.IsDigit).ToArray());
            if (digits.StartsWith("92") && digits.Length == 12) return "0" + digits[2..];
            return digits.Length > 0 ? "+" + digits : whatsAppPhone ?? "";
        }

        // ---- texts ------------------------------------------------------------------------------

        private string MainMenu() =>
            "Welcome to WorkNest Co-Working!\n\nHow can we help you today?\n\n" +
            "1. Book a Tour\n2. Complaints / Support\n3. Location & Directions\n\n" +
            "Reply with a number.\n\n" + SalesContacts();

        /// <summary>WhatsApp:SalesContacts ([{ "Label": "F-7, Islamabad", "Phone": "..." }]) or the default sales numbers.</summary>
        private string SalesContacts()
        {
            var configured = _config.GetSection("WhatsApp:SalesContacts").GetChildren()
                .Select(c => (Label: c["Label"], Phone: c["Phone"]))
                .Where(c => !string.IsNullOrWhiteSpace(c.Label) && !string.IsNullOrWhiteSpace(c.Phone)).ToList();
            if (configured.Count == 0)
                configured = new() { ("F-7, Islamabad", "03280256000"), ("I-8, Islamabad", "03201809696") };
            return "For direct contact to sales\n" + string.Join("\n", configured.Select(c => $"{c.Label}: {c.Phone}"));
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

        // ---- branches ---------------------------------------------------------------------------

        /// <summary>Active locations from dbo.WN_Locations in Id order (I-8 = 1, F-7 = 2), so "1. I-8 / 2. F-7".</summary>
        private async Task<List<Branch>> GetBranchesAsync()
        {
            try
            {
                var rows = await _db.GetAllLocationsAsync();
                var list = rows
                    .Select(r => new Branch(
                        r.TryGetValue("Id", out var id) && id is not null ? Convert.ToInt32(id) : 0,
                        r.TryGetValue("Name", out var n) ? n?.ToString()?.Trim() ?? "" : "",
                        r.TryGetValue("Address", out var a) ? a?.ToString()?.Trim() : null))
                    .Where(b => b.Name.Length > 0)
                    .OrderBy(b => b.Id)
                    .ToList();
                if (list.Count > 0) return list;
            }
            catch (Exception ex) { _logger.LogWarning(ex, "WhatsApp bot: could not load WN_Locations"); }
            return DefaultBranchOptions.Select((o, i) => new Branch(i + 1, o.Name, null)).ToList();
        }

        private WhatsAppBranchOptions Options(Branch b)
        {
            var configured = _config.GetSection("WhatsApp:Branches").GetChildren()
                .Select(c => new WhatsAppBranchOptions
                {
                    Name = c["Name"] ?? "",
                    Title = c["Title"],
                    Latitude = decimal.TryParse(c["Latitude"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var la) ? la : null,
                    Longitude = decimal.TryParse(c["Longitude"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lo) ? lo : null,
                    Phone = c["Phone"],
                    MapsUrl = c["MapsUrl"]
                });
            return configured.Concat(DefaultBranchOptions).FirstOrDefault(o => Norm(o.Name) == Norm(b.Name))
                   ?? new WhatsAppBranchOptions { Name = b.Name };
        }

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
