using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.Amenity;
using WorkNest.Application.Interfaces;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    public class AmenityController : ControllerBase
    {
        private readonly IAmenityService _amenities;
        public AmenityController(IAmenityService amenities) => _amenities = amenities;

        private const string AdminRoles = "admin,Admin,super_admin,SuperAdmin";

        [HttpGet("api/amenity")]
        [AllowAnonymous]
        public async Task<IActionResult> List() =>
            Ok(await _amenities.GetAllAmenitiesAsync());

        [HttpPost("api/amenity")]
        [Authorize(Roles = AdminRoles)]
        public async Task<IActionResult> Create([FromBody] AmenityUpsertRequest request) =>
            StatusCode(201, await _amenities.CreateAmenityAsync(request));
    }
}
