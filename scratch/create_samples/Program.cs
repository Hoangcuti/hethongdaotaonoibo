using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace CreateSamples
{
    class Program
    {
        static async Task Main(string[] args)
        {
            string apiKey = "AIzaSyD6fLc7mRhK0o134GrC326ArhTPydQA8Fs";
            Console.WriteLine("Listing models...");
            using (var httpClient = new HttpClient())
            {
                var url = $"https://generativelanguage.googleapis.com/v1beta/models?key={apiKey}";
                try
                {
                    var response = await httpClient.GetAsync(url);
                    Console.WriteLine("Response Status: " + response.StatusCode);
                    var responseJson = await response.Content.ReadAsStringAsync();
                    Console.WriteLine("API Response:");
                    Console.WriteLine(responseJson);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error: " + ex.Message);
                }
            }
        }
    }
}
