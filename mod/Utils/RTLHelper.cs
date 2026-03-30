using System;
using System.Globalization;
using System.Text;
using MelonLoader;

namespace AccessibilityMod.Utils
{
    /// <summary>
    /// Handles Right-to-Left text correction for screen reader output.
    ///
    /// The game uses two different text pipelines for Arabic:
    /// 1. I2 Localization applies Arabic shaping (presentation forms) AND reverses the string
    ///    for visual display in TMP — these strings need reversing back to logical order.
    /// 2. Some components use TMP's native RTL mode, which stores text in logical order
    ///    with presentation forms — these strings are already correct.
    ///
    /// We detect which pipeline was used by examining the FIRST Arabic Presentation Form
    /// character in the string. In I2-reversed text, it will be a FINAL form (because the
    /// last logical character was moved to the first position). In logical-order text,
    /// it will be an INITIAL or ISOLATED form.
    /// </summary>
    public static class RTLHelper
    {
        private static bool? _cachedIsRTL = null;
        private static string _cachedLanguage = null;

        /// <summary>
        /// Check if the current game language is RTL, using the game's own localization API.
        /// Result is cached until ClearCache() is called.
        /// </summary>
        public static bool IsCurrentLanguageRTL()
        {
            if (_cachedIsRTL.HasValue)
                return _cachedIsRTL.Value;

            try
            {
                bool isRTL = Il2CppLocalizationCustomSystem.LocalizationManager.IsRightToLeftText();
                _cachedLanguage = Il2CppLocalizationCustomSystem.LocalizationManager.GetCurrentLanguageName();
                _cachedIsRTL = isRTL;
                MelonLogger.Msg($"[RTL] Language: {_cachedLanguage}, RTL: {isRTL}");
                return isRTL;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[RTL] Could not detect RTL status: {ex.Message}");
                _cachedIsRTL = false;
                return false;
            }
        }

        /// <summary>
        /// Clear the cached RTL state. Call this when the language changes.
        /// </summary>
        public static void ClearCache()
        {
            _cachedIsRTL = null;
            _cachedLanguage = null;
        }

        /// <summary>
        /// Fix RTL text for screen reader output. Automatically detects whether the text
        /// is in visual (I2-reversed) or logical order by examining Arabic presentation
        /// form characters, and only reverses text that needs it.
        /// </summary>
        public static string FixForScreenReader(string text)
        {
            if (string.IsNullOrEmpty(text) || !IsCurrentLanguageRTL())
                return text;

            if (!ContainsRTLCharacters(text))
                return text;

            // Detect text direction from content.
            // I2-reversed text has a FINAL presentation form as its first shaped char.
            // Logical-order text has an INITIAL or ISOLATED form first.
            if (!IsVisualOrder(text))
            {
                if (UI.TextExtractor.DiagnosticLogging)
                    MelonLogger.Msg($"[RTL-FIX] Already logical: \"{(text.Length > 40 ? text.Substring(0, 40) + "..." : text)}\"");
                return text;
            }

            string result = ReverseString(text);

            if (UI.TextExtractor.DiagnosticLogging)
            {
                string before = text.Length > 30 ? text.Substring(0, 30) + "..." : text;
                string after = result.Length > 30 ? result.Substring(0, 30) + "..." : result;
                MelonLogger.Msg($"[RTL-FIX] Reversed: \"{before}\" → \"{after}\"");
            }

            return result;
        }

        /// <summary>
        /// Detect if Arabic text is in visual (I2-reversed) order using two signals:
        ///
        /// 1. Punctuation check: In I2-reversed text, sentence-ending punctuation (. ! ?)
        ///    moves to the START of the string. In logical Arabic, it's always at the end.
        ///    This catches cases where the first presentation form is an isolated form.
        ///
        /// 2. Presentation form check: In I2-reversed text, the first Arabic Presentation
        ///    Form character is a FINAL form (last logical char moved to first position).
        ///    In logical-order text, it's INITIAL or ISOLATED.
        /// </summary>
        private static bool IsVisualOrder(string text)
        {
            // Signal 1: Check if text starts with sentence-ending punctuation followed
            // by Arabic. In logical Arabic, sentences never start with a period.
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c)) continue;

                // If the first non-whitespace char is sentence-ending punctuation
                // and there are Arabic chars after it, this is visual order
                if (c == '.' || c == '!' || c == '?' || c == '\u061F') // U+061F = Arabic question mark
                {
                    if (ContainsRTLCharacters(text.Substring(i + 1)))
                        return true;
                }
                break; // Only check the first non-whitespace character
            }

            // Signal 2: Check the first Arabic Presentation Form character
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                // Only check Arabic Presentation Forms-B (where positional forms live)
                if (c >= 0xFE70 && c <= 0xFEFC)
                {
                    return IsArabicFinalForm(c);
                }
            }

            // No signals found — default to not reversing.
            return false;
        }

        /// <summary>
        /// Check if a character is an Arabic Presentation Forms-B FINAL positional form.
        /// These are the forms used at the END of a word in logical reading order.
        /// If one appears at the START of a string, the text was reversed for visual display.
        ///
        /// Reference: Unicode Standard, Arabic Presentation Forms-B (U+FE70-U+FEFF)
        /// Each Arabic letter has forms organized as: isolated(+0), final(+1), initial(+2), medial(+3)
        /// Letters with only 2 forms have: isolated(+0), final(+1)
        /// </summary>
        private static bool IsArabicFinalForm(char c)
        {
            switch (c)
            {
                // Alif variants: madda-final, hamza-above-final, waw-hamza-final, hamza-below-final
                case '\uFE82': case '\uFE84': case '\uFE86': case '\uFE88':
                // Yaa-hamza final, Alif final, Baa final, Taa-marbuta final
                case '\uFE8A': case '\uFE8E': case '\uFE90': case '\uFE94':
                // Taa final, Thaa final, Jeem final, Haa final
                case '\uFE96': case '\uFE9A': case '\uFE9E': case '\uFEA2':
                // Khaa final, Dal final, Thal final, Ra final
                case '\uFEA6': case '\uFEAA': case '\uFEAC': case '\uFEAE':
                // Zain final, Seen final, Sheen final, Sad final
                case '\uFEB0': case '\uFEB2': case '\uFEB6': case '\uFEBA':
                // Dad final, Tah final, Zah final, Ain final
                case '\uFEBE': case '\uFEC2': case '\uFEC6': case '\uFECA':
                // Ghain final, Fa final, Qaf final, Kaf final
                case '\uFECE': case '\uFED2': case '\uFED6': case '\uFEDA':
                // Lam final, Meem final, Noon final, Ha final
                case '\uFEDE': case '\uFEE2': case '\uFEE6': case '\uFEEA':
                // Waw final, Alif-maksura final, Ya final
                case '\uFEEE': case '\uFEF0': case '\uFEF2':
                // Lam-Alif ligature finals (madda, hamza-above, hamza-below, plain)
                case '\uFEF6': case '\uFEF8': case '\uFEFA': case '\uFEFC':
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Check if a string contains any RTL characters.
        /// </summary>
        private static bool ContainsRTLCharacters(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (IsRTLChar(text[i]))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Reverse the entire string, correctly handling surrogate pairs and
        /// combining characters via StringInfo.GetTextElementEnumerator.
        /// </summary>
        private static string ReverseString(string text)
        {
            var sb = new StringBuilder(text.Length);
            var elements = StringInfo.GetTextElementEnumerator(text);
            var textElements = new System.Collections.Generic.List<string>();
            while (elements.MoveNext())
            {
                textElements.Add(elements.GetTextElement());
            }
            for (int i = textElements.Count - 1; i >= 0; i--)
            {
                sb.Append(textElements[i]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Check if a character is in an RTL Unicode block (Arabic, Hebrew, etc.)
        /// </summary>
        private static bool IsRTLChar(char c)
        {
            return (c >= 0x0590 && c <= 0x05FF) ||  // Hebrew
                   (c >= 0x0600 && c <= 0x06FF) ||  // Arabic
                   (c >= 0x0750 && c <= 0x077F) ||  // Arabic Supplement
                   (c >= 0x08A0 && c <= 0x08FF) ||  // Arabic Extended-A
                   (c >= 0xFB50 && c <= 0xFDFF) ||  // Arabic Presentation Forms-A
                   (c >= 0xFE70 && c <= 0xFEFF);    // Arabic Presentation Forms-B
        }
    }
}
