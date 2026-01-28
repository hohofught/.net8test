using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GeminiWebTranslator.Models;

namespace GeminiWebTranslator.Services
{
    public class GeminiChatService
    {
        private readonly GeminiHttpClient _client;
        public List<ChatSessionInfo> Chats { get; private set; } = new List<ChatSessionInfo>();
        public string? CurrentChatId { get; set; }
        public string CurrentModel { get; set; } = "gemini-3.0-flash";

        public event Action<string>? OnLog;

        public GeminiChatService(GeminiHttpClient client)
        {
            _client = client;
        }

        private void Log(string msg) => OnLog?.Invoke($"[ChatService] {msg}");

        public async Task LoadChatsAsync(int limit = 20)
        {
            var rawChats = await _client.ListChatsAsync(limit);
            Chats.Clear();
            foreach (var c in rawChats)
            {
                if (c.TryGetValue("cid", out var cid))
                {
                    Chats.Add(new ChatSessionInfo
                    {
                        ChatId = cid,
                        Title = c.ContainsKey("title") ? c["title"] : "(No Title)"
                    });
                }
            }
            Log($"Loaded {Chats.Count} chats.");
        }

        public async Task<bool> DeleteChatAsync(string chatId)
        {
            var success = await _client.DeleteChatAsync(chatId);
            if (success)
            {
                Chats.RemoveAll(c => c.ChatId == chatId);
                if (CurrentChatId == chatId) CurrentChatId = null;
                Log($"Deleted chat {chatId}");
            }
            return success;
        }

        public async Task<string> SendMessageAsync(string message)
        {
            return await _client.GenerateContentAsync(message);
        }

        /// <summary>
        /// 특정 모델로 메시지를 전송합니다.
        /// </summary>
        public async Task<string> SendMessageWithModelAsync(string message, string? modelName = null)
        {
            var model = modelName ?? CurrentModel;
            return await _client.GenerateContentWithModelAsync(message, model);
        }

        /// <summary>
        /// 쿠키를 갱신합니다.
        /// </summary>
        public async Task<bool> RefreshCookiesAsync()
        {
            var newPsidts = await _client.RotateCookiesAsync();
            return !string.IsNullOrEmpty(newPsidts);
        }

        /// <summary>
        /// 이미지를 생성합니다.
        /// </summary>
        public async Task<List<string>> GenerateImageAsync(string prompt)
        {
            return await _client.GenerateImageAsync(prompt, CurrentModel);
        }
    }
}

