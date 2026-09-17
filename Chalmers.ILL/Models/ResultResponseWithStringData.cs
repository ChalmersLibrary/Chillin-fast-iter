using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace Chalmers.ILL.Models
{
    public class ResultResponseWithStringData
    {
        [Required]
        public bool Success { get; set; }
        public string Message { get; set; }
        public string Data { get; set; }
    }
}