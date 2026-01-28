namespace GeminiWebTranslator.Models
{
    public class ChatSessionInfo
    {
        public string ChatId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string LastMessage { get; set; } = string.Empty;
        public DateTime LastUpdated { get; set; } = DateTime.Now;
    }
}
