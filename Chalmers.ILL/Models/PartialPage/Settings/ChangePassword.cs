using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace Chalmers.ILL.Models.PartialPage.Settings
{
    public class ChangePassword
    {
        [Required]
        [Display(Name = "Nuvarande lösenord")]
        public string CurrentPassword { get; set; }

        [Required]
        [Display(Name = "Nytt lösenord")]
        public string NewPassword { get; set; }
    }
}