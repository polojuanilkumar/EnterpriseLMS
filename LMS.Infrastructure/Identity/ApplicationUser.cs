using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Infrastructure.Identity
{
    public class ApplicationUser : IdentityUser<Guid>
    {
        public string FirstName { get; set; } = string.Empty;

        public string LastName { get; set; } = string.Empty;
    }
}
