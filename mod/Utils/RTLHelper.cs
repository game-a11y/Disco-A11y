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
    /// We detect which pipeline was used via three signals: leading punctuation position,
    /// the first Arabic Presentation Form character type, and word-boundary positional
    /// form analysis. See IsVisualOrder() for details.
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

            // Reverse each line independently to preserve line order.
            // A full-string reverse would flip line order (Line1\nLine2 → Line2\nLine1).
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (ContainsRTLCharacters(lines[i]))
                    lines[i] = ReverseLine(lines[i]);
            }
            string result = string.Join("\n", lines);

            if (UI.TextExtractor.DiagnosticLogging)
            {
                string before = text.Length > 30 ? text.Substring(0, 30) + "..." : text;
                string after = result.Length > 30 ? result.Substring(0, 30) + "..." : result;
                MelonLogger.Msg($"[RTL-FIX] Reversed: \"{before}\" → \"{after}\"");
            }

            return result;
        }

        /// <summary>
        /// Detect if Arabic text is in visual (I2-reversed) order using three signals:
        ///
        /// 1. Punctuation check: In I2-reversed text, sentence-ending punctuation (. ! ? : ;)
        ///    moves to the START of the string. Skips leading quotation marks first.
        ///    In logical Arabic, these marks are always at the end, never the start.
        ///
        /// 2. Presentation form check: In I2-reversed text, the first Arabic Presentation
        ///    Form character is a FINAL form (last logical char moved to first position).
        ///    In logical-order text, it's INITIAL or ISOLATED.
        ///
        /// 3. Word-boundary check: In logical Arabic, a letter immediately after a space
        ///    is always INITIAL or ISOLATED (start of word). In I2-reversed text, these
        ///    positions contain FINAL or MEDIAL forms. This signal checks multiple word
        ///    boundaries for high confidence.
        /// </summary>
        private static bool IsVisualOrder(string text)
        {
            // Signal 1: Check if text starts with sentence-ending punctuation followed
            // by Arabic. Skip leading whitespace and quotation marks first.
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c)) continue;

                // Skip quotation marks — these are directionally neutral and can wrap
                // either logical or visual text
                if (c == '"' || c == '\'' || c == '\u201C' || c == '\u201D' || // " " ' "
                    c == '\u2018' || c == '\u2019' || c == '\u00AB' || c == '\u00BB') // ' ' « »
                    continue;

                // If the first non-whitespace, non-quote char is sentence-ending punctuation
                // and there are Arabic chars after it, this is visual order
                if (c == '.' || c == '!' || c == '?' || c == '\u061F' || // U+061F = Arabic question mark
                    c == ':' || c == '\u061B') // U+061B = Arabic semicolon
                {
                    if (ContainsRTLCharacters(text.Substring(i + 1)))
                        return true;
                }
                break; // Only check the first significant character
            }

            // Signal 2: Check the first Arabic Presentation Form character
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                // Only check Arabic Presentation Forms-B (where positional forms live)
                if (c >= 0xFE70 && c <= 0xFEFC)
                {
                    if (IsArabicFinalForm(c))
                        return true;
                    break; // First form found but not final — check signal 3
                }
            }

            // Signal 3: Check word boundaries. In logical Arabic, the first letter of each
            // word (after a space) is always in INITIAL or ISOLATED form. In I2-reversed text,
            // these positions contain FINAL or MEDIAL forms because the word order is inverted.
            bool afterSpace = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == ' ')
                {
                    afterSpace = true;
                    continue;
                }
                if (afterSpace && c >= 0xFE70 && c <= 0xFEFC)
                {
                    if (IsArabicFinalForm(c) || IsArabicMedialForm(c))
                        return true;
                    afterSpace = false;
                }
                else
                {
                    afterSpace = false;
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
        /// Check if a character is an Arabic Presentation Forms-B MEDIAL positional form.
        /// These appear in the MIDDLE of a word in logical reading order.
        /// Like final forms, they should never appear at the start of a word.
        ///
        /// Only letters with 4 forms have medial variants.
        /// Pattern: isolated(+0), final(+1), initial(+2), medial(+3)
        /// </summary>
        private static bool IsArabicMedialForm(char c)
        {
            switch (c)
            {
                // Yaa-hamza medial, Baa medial, Taa-marbuta medial (rare), Taa medial
                case '\uFE8C': case '\uFE92': case '\uFE98':
                // Thaa medial, Jeem medial, Haa medial, Khaa medial
                case '\uFE9C': case '\uFEA0': case '\uFEA4': case '\uFEA8':
                // Seen medial, Sheen medial, Sad medial, Dad medial
                case '\uFEB4': case '\uFEB8': case '\uFEBC': case '\uFEC0':
                // Tah medial, Zah medial, Ain medial, Ghain medial
                case '\uFEC4': case '\uFEC8': case '\uFECC': case '\uFED0':
                // Fa medial, Qaf medial, Kaf medial, Lam medial
                case '\uFED4': case '\uFED8': case '\uFEDC': case '\uFEE0':
                // Meem medial, Noon medial, Ha medial, Ya medial
                case '\uFEE4': case '\uFEE8': case '\uFEEC': case '\uFEF4':
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
        /// Reverse a single line of text, correctly handling surrogate pairs,
        /// combining characters, and LTR number runs.
        ///
        /// When I2 Localization reverses Arabic text for visual display, it also
        /// reverses embedded number sequences (e.g. "09-21" becomes "12-90"). We
        /// collect text elements, reverse their order, then re-reverse any LTR runs
        /// (digits plus connecting punctuation like hyphens, colons, periods between
        /// digits) so numbers and dates read correctly in the final output.
        /// </summary>
        private static string ReverseLine(string text)
        {
            var elements = StringInfo.GetTextElementEnumerator(text);
            var textElements = new System.Collections.Generic.List<string>();
            while (elements.MoveNext())
            {
                textElements.Add(elements.GetTextElement());
            }
            textElements.Reverse();

            // Fix LTR runs: sequences of digits (possibly connected by punctuation
            // like - . : / between digits) need to be re-reversed so they read LTR.
            var sb = new StringBuilder(text.Length);
            int i = 0;
            while (i < textElements.Count)
            {
                if (IsDigitElement(textElements[i]))
                {
                    int start = i;
                    i++;
                    while (i < textElements.Count)
                    {
                        if (IsDigitElement(textElements[i]))
                        {
                            i++;
                        }
                        else if (IsNumberConnector(textElements[i]) &&
                                 i + 1 < textElements.Count &&
                                 IsDigitElement(textElements[i + 1]))
                        {
                            // Include connector (e.g. hyphen) only if followed by more digits
                            i++;
                        }
                        else
                        {
                            break;
                        }
                    }
                    // Re-reverse the LTR run
                    for (int j = i - 1; j >= start; j--)
                        sb.Append(textElements[j]);
                }
                else
                {
                    sb.Append(textElements[i]);
                    i++;
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Check if a text element is a digit character (ASCII 0-9 or Arabic-Indic digits).
        /// </summary>
        private static bool IsDigitElement(string element)
        {
            if (element.Length != 1) return false;
            char c = element[0];
            return (c >= '0' && c <= '9') ||        // ASCII digits
                   (c >= '\u0660' && c <= '\u0669') || // Arabic-Indic digits ٠-٩
                   (c >= '\u06F0' && c <= '\u06F9');   // Extended Arabic-Indic digits ۰-۹
        }

        /// <summary>
        /// Check if a text element is punctuation that connects parts of a number
        /// (e.g. hyphens in dates "09-21", colons in times "12:30", periods in
        /// decimals "3.5", slashes in dates "09/21"). These should stay inside
        /// an LTR run when they appear between digits.
        /// </summary>
        private static bool IsNumberConnector(string element)
        {
            if (element.Length != 1) return false;
            char c = element[0];
            return c == '-' || c == '.' || c == ':' || c == '/';
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
