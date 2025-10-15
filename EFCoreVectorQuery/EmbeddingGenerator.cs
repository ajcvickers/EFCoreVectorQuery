using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

// Adapted from https://www.mongodb.com/docs/atlas/atlas-vector-search/create-embeddings
public class EmbeddingGenerator(string voyageApiKey)
{
    private static readonly string EmbeddingModelName = "voyage-3-large";
    private static readonly string ApiEndpoint = "https://api.voyageai.com/v1/embeddings";

    public async Task GetEmbeddingsAsync(string[] texts)
    {
        using HttpClient client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", voyageApiKey);

        var requestBody = new
        {
            input = texts,
            model = EmbeddingModelName,
            truncation = true,
            output_dimension = 2048
        };

        var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
        var response = await client.PostAsync(ApiEndpoint, content);

        if (response.IsSuccessStatusCode)
        {
            string responseBody = await response.Content.ReadAsStringAsync();
            var embeddingResponse = JsonSerializer.Deserialize<EmbeddingResponse>(responseBody)!;

            foreach (var embeddingResult in embeddingResponse.Data!)
            {
                var embedding = embeddingResult.Embedding.Select(e => (float)e).ToArray();
                Console.WriteLine("public static readonly float[] Embedding = new []{ " + string.Join(", ", embedding.Select(e => $"{e}f")) + " };");
            }
        }
        else
        {
            throw new ApplicationException($"Error calling Voyage API: {response.ReasonPhrase}");
        }
    }

    private class EmbeddingResponse
    {
        [JsonPropertyName("object")]
        public string Object { get; set; } = string.Empty;
        
        [JsonPropertyName("data")]
        public List<EmbeddingResult>? Data { get; set; }
        
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;
        
        [JsonPropertyName("usage")]
        public Usage? Usage { get; set; }
    }
    
    private class EmbeddingResult
    {
        [JsonPropertyName("object")]
        public string Object { get; set; } = string.Empty;
        
        [JsonPropertyName("embedding")]
        public List<double> Embedding { get; set; } = new();
        
        [JsonPropertyName("index")]
        public int Index { get; set; }
    }
    
    private class Usage
    {
        [JsonPropertyName("total_tokens")]
        public int TotalTokens { get; set; }
    }
}