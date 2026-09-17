using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Chalmers.ILL.Utilities
{

    /// <summary>
    /// This class contains different helpers that we can use to make life easier.
    /// </summary>
    /// 
    public class Helpers
    {
        public static string CalculateMD5Hash(string input)
        {
            // step 1, calculate MD5 hash from input
            MD5 md5 = System.Security.Cryptography.MD5.Create();
            byte[] inputBytes = System.Text.Encoding.ASCII.GetBytes(input);
            byte[] hash = md5.ComputeHash(inputBytes);

            // step 2, convert byte array to hex string
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < hash.Length; i++)
            {
                sb.Append(hash[i].ToString("x2"));
            }
            return sb.ToString();
        }

        public static string HtmlToPlainText(string html)
        {
            const string tagWhiteSpace = @"(\r\n)+";
            const string initialLineBreak = @"^(\n)+";
            const string emailFormatStart = @"(&lt;\n)";
            const string emailFormatEnd = @"(\n&gt;)";
            var tagWhiteSpaceRegex = new Regex(tagWhiteSpace, RegexOptions.Multiline);
            var initialLineBreakRegex = new Regex(initialLineBreak, RegexOptions.Multiline);
            var emailFormatStartRegex = new Regex(emailFormatStart, RegexOptions.Multiline);
            var emailFormatEndRegex = new Regex(emailFormatEnd, RegexOptions.Multiline);

            var text = html;

            //Decode html specific characters
            // text = System.Net.WebUtility.HtmlDecode(text);

            //Remove tag whitespace/line breaks/other shit
            text = tagWhiteSpaceRegex.Replace(text, "\n");
            text = initialLineBreakRegex.Replace(text, "");
            text = emailFormatStartRegex.Replace(text, "&lt;");
            text = emailFormatEndRegex.Replace(text, "&gt;");

            return text;
        }
    }
}