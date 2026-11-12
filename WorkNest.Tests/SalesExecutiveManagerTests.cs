using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using WorkNest.Application.DTOs.Auth;
using WorkNest.Application.DTOs.Dashboard;
using WorkNest.Application.DTOs.User;
using WorkNest.Application.Interfaces;
using WorkNest.Application.Services;

namespace WorkNest.Tests
{
    public class SalesExecutiveManagerTests
    {
        [Fact]
        public async Task CreateRejectsManagerWhoIsInactiveOrHasWrongRole()
        {
            foreach (var manager in new[]
            {
                UserRow(20, 18, false, 1),
                UserRow(20, 16, true, 1)
            })
            {
                var db = CreateUserRepository();
                db.Setup(x => x.GetUserByIdAsync(20)).ReturnsAsync(manager);
                var service = new UserService(db.Object);

                var result = await service.CreateUserAsync(ExecutiveRequest(20, 1, new[] { 1 }), null);

                Assert.False(result.IsSuccessful);
                db.Verify(x => x.CreateUserAsync(
                    It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                    It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                    It.IsAny<int?>(), It.IsAny<IReadOnlyCollection<int>?>()), Times.Never);
            }
        }

        [Fact]
        public async Task CreateAcceptsManagerSharingExecutivePrimaryLocation()
        {
            var db = CreateUserRepository();
            db.Setup(x => x.GetUserByIdAsync(20)).ReturnsAsync(UserRow(20, 18, true, 7));
            db.Setup(x => x.GetUserLocationIdsAsync(20)).ReturnsAsync(new List<int>());
            var service = new UserService(db.Object);

            var result = await service.CreateUserAsync(ExecutiveRequest(20, 7, new[] { 7 }), null);

            Assert.True(result.IsSuccessful);
            db.Verify(x => x.CreateUserAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(), 7, 20,
                It.Is<IReadOnlyCollection<int>?>(ids => ids != null && ids.Contains(7))), Times.Once);
        }

        [Fact]
        public async Task CreateAcceptsManagerSharingSecondaryUserLocation()
        {
            var db = CreateUserRepository();
            db.Setup(x => x.GetUserByIdAsync(20)).ReturnsAsync(UserRow(20, 18, true, 90));
            db.Setup(x => x.GetUserLocationIdsAsync(20)).ReturnsAsync(new List<int> { 22 });
            var service = new UserService(db.Object);

            var result = await service.CreateUserAsync(ExecutiveRequest(20, 10, new[] { 10, 22 }), null);

            Assert.True(result.IsSuccessful);
        }

        [Fact]
        public async Task CreateRejectsManagerForNonExecutiveRole()
        {
            var db = CreateUserRepository();
            var service = new UserService(db.Object);
            var request = new UserCreateRequest
            {
                Email = "admin@example.com",
                Role = "admin",
                LocationId = 1,
                LocationIds = new List<int> { 1 },
                ManagerId = 20
            };

            var result = await service.CreateUserAsync(request, null);

            Assert.False(result.IsSuccessful);
            db.Verify(x => x.CreateUserAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<int?>(), It.IsAny<IReadOnlyCollection<int>?>()), Times.Never);
        }

        [Fact]
        public async Task CreateWithNullManagerDoesNotBlockExecutive()
        {
            var db = CreateUserRepository();
            var service = new UserService(db.Object);

            var result = await service.CreateUserAsync(ExecutiveRequest(null, 3, new[] { 3 }), null);

            Assert.True(result.IsSuccessful);
            db.Verify(x => x.GetUserByIdAsync(It.IsAny<int>()), Times.Never);
            db.Verify(x => x.CreateUserAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(), 3, null,
                It.IsAny<IReadOnlyCollection<int>?>()), Times.Once);
        }

        [Fact]
        public async Task UpdateClearsExistingManagerWhenExecutiveChangesToUnsharedLocations()
        {
            var db = CreateUserRepository();
            db.Setup(x => x.GetUserByIdAsync(1)).ReturnsAsync(UserRow(1, 16, true, 10));
            db.Setup(x => x.GetUserByIdAsync(20)).ReturnsAsync(UserRow(20, 18, true, 10));
            db.Setup(x => x.GetUserLocationIdsAsync(1)).ReturnsAsync(new List<int> { 10 });
            db.Setup(x => x.GetUserLocationIdsAsync(20)).ReturnsAsync(new List<int>());
            db.Setup(x => x.GetUserManagerMapAsync(It.IsAny<IEnumerable<int>>()))
                .ReturnsAsync(new Dictionary<int, (int? ManagerId, string? ManagerName)> { [1] = (20, "Manager") });
            var service = new UserService(db.Object);

            var result = await service.UpdateUserAsync(1, new UserUpdateRequest
            {
                LocationId = 30,
                LocationIds = new List<int> { 30 }
            });

            Assert.True(result.IsSuccessful);
            db.Verify(x => x.UpdateUserAsync(1, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(),
                It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                30, It.IsAny<string?>(), null, true,
                It.Is<IReadOnlyCollection<int>?>(ids => ids != null && ids.SequenceEqual(new[] { 30 })), true, true), Times.Once);
        }

        [Fact]
        public async Task RoleChangeAwayFromExecutiveClearsExistingManager()
        {
            var db = CreateUserRepository();
            db.Setup(x => x.GetUserByIdAsync(1)).ReturnsAsync(UserRow(1, 16, true, 10));
            db.Setup(x => x.GetUserLocationIdsAsync(1)).ReturnsAsync(new List<int> { 10 });
            db.Setup(x => x.GetUserManagerMapAsync(It.IsAny<IEnumerable<int>>()))
                .ReturnsAsync(new Dictionary<int, (int? ManagerId, string? ManagerName)> { [1] = (20, "Manager") });
            var service = new UserService(db.Object);

            var result = await service.UpdateUserAsync(1, new UserUpdateRequest
            {
                Role = "sales_manager",
                LocationId = 2,
                LocationIds = new List<int> { 2 }
            });

            Assert.True(result.IsSuccessful);
            db.Verify(x => x.SetUserRoleAndLocationAsync(1, 18, 2, null, true,
                It.Is<IReadOnlyCollection<int>?>(ids => ids != null && ids.SequenceEqual(new[] { 2 })), true, true), Times.Once);
        }

        [Fact]
        public async Task DeactivationUsesRepositoryCleanupPath()
        {
            var db = new Mock<IDbRepository>();
            var service = new UserService(db.Object);

            var result = await service.DeactivateUserAsync(12);

            Assert.True(result.IsSuccessful);
            db.Verify(x => x.SetUserStatusAsync(12, false), Times.Once);
        }

        [Fact]
        public async Task DashboardTeamContainsAssignedAndUnassignedExecutivesOnly()
        {
            var repo = new Mock<ISalesManagerDashboardRepository>();
            var clock = new Mock<IBusinessClock>();
            clock.SetupGet(x => x.Today).Returns(new DateTime(2026, 3, 10));
            clock.SetupGet(x => x.Now).Returns(new DateTime(2026, 3, 10, 12, 0, 0));
            var results = Enumerable.Range(0, 12).Select(_ => new List<IDictionary<string, object?>>()).ToList();
            results[1].Add(new Dictionary<string, object?> { ["Quotations"] = 17 });
            results[3].Add(TeamRow(1, 16, 42));
            results[3].Add(TeamRow(2, 16, null));
            results[3].Add(TeamRow(3, 16, 99));
            results[3].Add(TeamRow(4, 2, 42));
            repo.Setup(x => x.GetDashboardAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>(),
                    It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), 42))
                .ReturnsAsync(results);
            var service = new SalesManagerDashboardService(repo.Object, clock.Object);

            var result = await service.GetDashboardAsync("month", new SalesManagerScope(new[] { 1 }, null), currentManagerId: 42);

            Assert.True(result.IsSuccessful);
            var dashboard = Assert.IsType<SalesManagerDashboardDto>(result.Data);
            Assert.Equal(new[] { 1, 2 }, dashboard.Team.People.Select(x => x.UserId));
            Assert.Equal(17, dashboard.Funnel.Stages.Single(x => x.Stage == "quotations").Count);
        }

        private static UserCreateRequest ExecutiveRequest(int? managerId, int primaryLocation, IEnumerable<int> locationIds) => new()
        {
            Email = "executive@example.com",
            Name = "Executive",
            Role = "sales_executive",
            LocationId = primaryLocation,
            LocationIds = locationIds.ToList(),
            ManagerId = managerId
        };

        private static Dictionary<string, object?> UserRow(int id, int roleId, bool active, int locationId) => new()
        {
            ["Id"] = id,
            ["RoleId"] = roleId,
            ["IsActive"] = active,
            ["LocationId"] = locationId
        };

        private static IDictionary<string, object?> TeamRow(int id, int roleId, int? managerId) => new Dictionary<string, object?>
        {
            ["UserId"] = id,
            ["Name"] = $"User {id}",
            ["RoleId"] = roleId,
            ["ManagerId"] = managerId,
            ["RoleName"] = "Sales Executive"
        };

        private static Mock<IDbRepository> CreateUserRepository()
        {
            var db = new Mock<IDbRepository>();
            db.Setup(x => x.GetUserSignInStatusAsync(It.IsAny<string>()))
                .ReturnsAsync((UserSignInStatus?)null);
            db.Setup(x => x.GetCustomerByEmailAsync(It.IsAny<string>()))
                .ReturnsAsync((IDictionary<string, object?>?)null);
            db.Setup(x => x.CreateCustomerAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(),
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(),
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
                    It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
                .ReturnsAsync(new Dictionary<string, object?>());
            db.Setup(x => x.CreateUserAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
                    It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                    It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<IReadOnlyCollection<int>?>()))
                .ReturnsAsync(((int?)123, (string?)"public-id"));
            db.Setup(x => x.SetUserLocationsAsync(It.IsAny<int>(), It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<int?>()))
                .ReturnsAsync(true);
            return db;
        }
    }
}
