using LinkLab.Api.Data;
using Microsoft.EntityFrameworkCore;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

var jwtIssuer = builder.Configuration["Jwt:Issuer"];
var jwtAudience = builder.Configuration["Jwt:Audience"];
var jwtKey = builder.Configuration["Jwt:Key"];

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,

            ValidateAudience = true,
            ValidAudience = jwtAudience,

            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey!)),

            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2)
        };

        // ✅ DEV ONLY: allow JWT from cookie so Swagger stays "logged in"
        if (builder.Environment.IsDevelopment())
        {
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    // Prefer normal Authorization header behavior if present
                    var authHeader = context.Request.Headers.Authorization.ToString();
                    if (!string.IsNullOrWhiteSpace(authHeader))
                        return Task.CompletedTask;

                    // Otherwise try cookie
                    if (context.Request.Cookies.TryGetValue("access_token", out var token)
                        && !string.IsNullOrWhiteSpace(token))
                    {
                        context.Token = token;
                    }

                    return Task.CompletedTask;
                }
            };
        }
    });

builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();