using System;
using System.Text.RegularExpressions;

namespace YIS.Helpers
{
    public class SmartSearchResult
    {
        public string RawTerm { get; set; } = string.Empty;
        public string CleanTerm { get; set; } = string.Empty;
        public long? ExtractedNumber { get; set; }
        public string? DetectedPrefix { get; set; }
        public bool HasNumber => ExtractedNumber.HasValue;
        public bool IsEmpty => string.IsNullOrWhiteSpace(RawTerm);
    }

    public static class SmartSearchHelper
    {
        // Matches patterns like: "pur1", "pur-1", "pur 1", "PUR-001", "#1", "sal-25", "102"
        private static readonly Regex CodePattern = new Regex(
            @"^(?:(?<prefix>[a-zA-Z]{2,5})[\s\-\#\/\:]*)?(?<num>\d+)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );

        public static SmartSearchResult Parse(string? input)
        {
            var result = new SmartSearchResult();
            if (string.IsNullOrWhiteSpace(input))
            {
                return result;
            }

            result.RawTerm = input.Trim();
            string trimmed = result.RawTerm;

            var match = CodePattern.Match(trimmed);
            if (match.Success)
            {
                string numStr = match.Groups["num"].Value;
                if (long.TryParse(numStr, out long parsedNum))
                {
                    result.ExtractedNumber = parsedNum;
                }

                if (match.Groups["prefix"].Success && !string.IsNullOrWhiteSpace(match.Groups["prefix"].Value))
                {
                    result.DetectedPrefix = match.Groups["prefix"].Value.ToUpperInvariant();
                }

                // If a pure number or prefix+number was typed, clean term is the number itself
                result.CleanTerm = numStr;
            }
            else
            {
                // Pure textual search (e.g. party name, item, remarks)
                result.CleanTerm = trimmed.ToLowerInvariant();
            }

            return result;
        }
    }
}
