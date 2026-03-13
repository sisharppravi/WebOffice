using bsckend.Models.DTOs.AuthDTOs;
using bsckend.Models.User;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace bsckend.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController: ControllerBase
{
    private readonly ILogger<AuthController> _logger;
    private readonly UserManager<UserModel> _userManager;
    
    public AuthController(ILogger<AuthController> logger, UserManager<UserModel> userManager)
    {
        _logger = logger;
        _userManager = userManager;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterDto dto)
    {
        _logger.LogInformation("Registering new user with login: {Login}",  dto.Login);
        var user = new UserModel
        {
            UserName = dto.Login
        };
        var result = await _userManager.CreateAsync(user, dto.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                _logger.LogWarning(
                    "Registration failed for user {Login}. Error: {Error}",
                    dto.Login,
                    error.Description);
            }

            return BadRequest(result.Errors);
        }

        _logger.LogInformation("User {Login} successfully registered with id {UserId}", 
            dto.Login, user.Id);

        return Ok(new
        {
            message = "User created"
        });
    }
    
}