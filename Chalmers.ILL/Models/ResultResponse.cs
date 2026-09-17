using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace Chalmers.ILL.Models
{
    /// <summary>
    /// General Response Object to use in CRUD communication
    /// </summary>
    public class ResultResponse
    {
        public ResultResponse()
        {
            // NOP
        }

        public ResultResponse(bool success, string message)
        {
            Success = success;
            Message = message;
        }

        [Required]
        public bool Success { get; set; }
        public string Message { get; set; }
    }
}