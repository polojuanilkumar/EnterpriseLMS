using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Infrastructure.Identity
{
    public static class IdentitySeeder
    {
        private static readonly string[] Roles =
        [
            "Student",
        "Instructor",
        "Admin",
        "SuperAdmin"
        ];

        public static async Task SeedRolesAsync(
            RoleManager<IdentityRole<Guid>> roleManager)
        {
            foreach (var role in Roles)
            {
                if (await roleManager.RoleExistsAsync(role))
                {
                    continue;
                }

                var result = await roleManager.CreateAsync(
                    new IdentityRole<Guid>(role));

                if (!result.Succeeded)
                {
                    var errors = string.Join(
                        ", ",
                        result.Errors.Select(x => x.Description));

                    throw new InvalidOperationException(
                        $"Failed to create role '{role}': {errors}");
                }
            }
        }
    }
}
