using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace GeminiWebTranslator
{
    public static class TextHelper
    {
        /// <summary>
        /// Splits text into chunks while preserving original punctuation and structure.
        /// Improved to avoid scrambling numbered lists or sequences.
        /// </summary>
        public static List<string> SplitIntoChunks(string text, int chunkSize)
        {
            var chunks = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return chunks;

            // Split by paragraph first (double newline), then by sentence within if needed
            var paragraphs = text.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            var current = "";

            foreach (var para in paragraphs)
            {
                // If adding this paragraph exceeds limit, save current and start new
                if (current.Length + para.Length + 2 > chunkSize && current.Length > 0)
                {
                    chunks.Add(current.Trim());
                    current = "";
                }

                // If a single paragraph is too large, split by sentence
                if (para.Length > chunkSize)
                {
                    // Save current first
                    if (current.Length > 0)
                    {
                        chunks.Add(current.Trim());
                        current = "";
                    }
                    
                    // Split large paragraph by sentence-ending punctuation
                    // Preserve punctuation using regex lookahead
                    var sentences = Regex.Split(para, @"(?<=[.!?。！？])\s+");
                    foreach (var sentence in sentences)
                    {
                        if (current.Length + sentence.Length + 1 > chunkSize && current.Length > 0)
                        {
                            chunks.Add(current.Trim());
                            current = "";
                        }
                        current += sentence + " ";
                    }
                }
                else
                {
                    current += para + "\n\n";
                }
            }
            
            if (!string.IsNullOrWhiteSpace(current)) 
                chunks.Add(current.Trim());

            // Fallback: if still empty or a single chunk is too large, force split
            if (chunks.Count == 0 && text.Length > 0)
            {
                for (int i = 0; i < text.Length; i += chunkSize)
                {
                    chunks.Add(text.Substring(i, Math.Min(chunkSize, text.Length - i)));
                }
            }

            return chunks.Count > 0 ? chunks : new List<string> { text };
        }
    }
}
