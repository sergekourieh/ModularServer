using System.Text;
using System.Text.Json;

namespace Server {
    public class OpenAIAPI
    {
        #region Attributes
        private readonly HttpClient _httpClient;
        private readonly string _model;
        #endregion

        public OpenAIAPI(string apiKey, string model = "gpt-5-mini")
        {
            _httpClient = new HttpClient();
            _model = model;
            
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        }

        public class Resource
        {
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
        }

        public class ScriptSuggestion
        {
            public string ScriptName { get; set; } = string.Empty;
            public string Purpose { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public List<string> RelatedResources { get; set; } = new();
        }

        public async Task<List<ScriptSuggestion>> SuggestScriptsAsync(List<Resource> resources, int maxSuggestions = 5) {
            var systemPrompt = BuildSystemPrompt();
            var userPrompt = BuildUserPrompt(resources, maxSuggestions);

            var requestBody = new {
                model = _model,
                messages = new[] {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                max_completion_tokens = 2000
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode) {
                throw new Exception($"OpenAI API error: {response.StatusCode} - {responseContent}");
            }

            using var document = JsonDocument.Parse(responseContent);
            var messageContent = document.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return ParseSuggestions(messageContent ?? string.Empty);
        }

        public string? chatGPT(string msg) {
            if (string.IsNullOrEmpty(msg) || string.IsNullOrWhiteSpace(msg))
                return null;
            var requestBody = new {
                model = _model,
                messages = new[] {
                    new { role = "user", content = msg },
                },
                max_completion_tokens = 2000
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content).GetAwaiter().GetResult();
            var responseContent = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode) {
                throw new Exception($"OpenAI API error: {response.StatusCode} - {responseContent}");
            }

            using var document = JsonDocument.Parse(responseContent);
            var messageContent = document.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
            return messageContent;
        }

        private string BuildSystemPrompt() {
        return @"You are a Lua scripting expert for general-purpose(Can be any kind of server, understand based on resources names) servers. Suggest practical Lua scripts based on existing resources. 
Respond ONLY with a JSON array in this format:
[{ ""ScriptName"": ""descriptive_script_name"",""Purpose"": ""What this script accomplishes"",""Description"": ""Detailed functionality"",""RelatedResources"": [""resource1"", ""resource2""]}]
Focus on automation, utilities, and quality-of-life improvements. Keep names descriptive and lowercase_with_underscores. Maximum 5 suggestions.
Also, keep the description as minimal as you can!";
        }

        private string BuildUserPrompt(List<Resource> resources, int maxSuggestions) {
            var prompt = $"Based on these {resources.Count} resources:\n";

            foreach (var resource in resources) {
                // Keeping it minimal for price: only name + description
                prompt += $"{resource.Name}: {resource.Description}\n";
            }

            prompt += $"\nSuggest {maxSuggestions} new Lua scripts to complement these resources.";
            return prompt;
        }

        private List<ScriptSuggestion> ParseSuggestions(string responseContent) {
            try {
                var jsonStart = responseContent.IndexOf('[');
                var jsonEnd = responseContent.LastIndexOf(']') + 1;

                if (jsonStart >= 0 && jsonEnd > jsonStart) { //Check there's a response
                    var jsonContent = responseContent.Substring(jsonStart, jsonEnd - jsonStart);
                    var options = new JsonSerializerOptions {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        PropertyNameCaseInsensitive = true
                    };
                    return JsonSerializer.Deserialize<List<ScriptSuggestion>>(jsonContent, options) ?? new List<ScriptSuggestion>();
                }
                return new List<ScriptSuggestion>(); //If there's no response, return empty
            } catch (Exception ex) {
                Console.WriteLine($"Error parsing OpenAI response: {ex.Message}");
                Console.WriteLine($"Response content: {responseContent}");
                return new List<ScriptSuggestion>(); //Error in the response, return empty
            }
        }
    }
}