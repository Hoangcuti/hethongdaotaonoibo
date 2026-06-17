using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using System.Linq;
using Xceed.Words.NET;
using ClosedXML.Excel;

namespace KhoaHoc.Services;

public class GeminiAIService : IAIService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<GeminiAIService> _logger;

    public GeminiAIService(HttpClient httpClient, IConfiguration config, ILogger<GeminiAIService> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
    }

    public async Task<GeneratedCourseDto> GenerateCourseContentAsync(string topic)
    {
        var apiKey = _config["GeminiAI:ApiKey"];
        var model = _config["GeminiAI:Model"] ?? "gemini-1.5-flash";

        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("Gemini API Key is missing. Using Mock Mode for GenerateCourseContentAsync.");
            return GetMockGeneratedCourse(topic);
        }

        try
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
            
            var prompt = $@"Bạn là Giám đốc Đào tạo ưu tú của một Tập đoàn lớn.
Nhiệm vụ của bạn là soạn thảo cấu trúc khóa học nội bộ cho nhân sự về chủ đề: '{topic}'. KHÔNG SỬ DỤNG FORMAT MARKDOWN HOẶC CÁC KÍ TỰ ĐẶC BIỆT THỪA VÌ SẼ BỊ LỖI JSON PARSE.
Vui lòng trả về kết quả 100% dưới định dạng JSON hợp lệ cực kỳ nghiêm ngặt như mẫu sau:
{{
  ""Title"": ""Khóa học: Tên khóa học ngắn gọn"",
  ""Description"": ""Mô tả lợi ích khóa học từ góc độ thực tiễn doanh nghiệp"",
  ""Modules"": [
    {{
      ""Title"": ""Chương 1: Tiêu đề chương"",
      ""LessonTitles"": [""Bài 1.1: Tên bài"", ""Bài 1.2: Tên bài""]
    }}
  ]
}}";

            var requestBody = new
            {
                contents = new[]
                {
                    new { parts = new[] { new { text = prompt } } }
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorText = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Gemini API Error: {errorText}");
                return GetMockGeneratedCourse(topic);
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);
            
            var textResult = doc.RootElement
                                .GetProperty("candidates")[0]
                                .GetProperty("content")
                                .GetProperty("parts")[0]
                                .GetProperty("text")
                                .GetString();

            if (textResult == null) return GetMockGeneratedCourse(topic);

            // Xử lý json bị bọc ngoài bởi markdown
            var cleanJson = ExtractJson(textResult);
            
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<GeneratedCourseDto>(cleanJson, options);
            
            return result ?? GetMockGeneratedCourse(topic);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error calling AI: {ex.Message}");
            return GetMockGeneratedCourse(topic);
        }
    }

    public async Task<GeneratedQuizDto> GenerateQuizAsync(string topic)
    {
        var apiKey = _config["GeminiAI:ApiKey"];
        var model = _config["GeminiAI:Model"] ?? "gemini-1.5-flash";

        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("Gemini API Key missing. Using Mock Mode for GenerateQuizAsync.");
            return GetMockGeneratedQuiz(topic);
        }

        try
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
            
            var prompt = $@"Bạn là một Chuyên gia khảo thí giáo dục chuyên nghiệp.
Nhiệm vụ: Hãy soạn thảo một bộ đề thi/kiểm tra tối đa 10 câu hỏi về chủ đề: '{topic}'.
Đề thi cần kiểm tra các kiến thức cốt lõi. Có 3 loại câu hỏi: Trắc nghiệm (MultipleChoice - có thể chọn 1 hoặc nhiều đáp án đúng), Điền vào chỗ trống (FillInTheBlank - có 1 ô nhập đáp án đúng), Tự luận (Essay - bài tự luận chấm tay). Hãy phân bổ hợp lý các loại câu hỏi này (ưu tiên Trắc nghiệm và Điền khuyết).

Yêu cầu trả về kết quả 100% dưới định dạng JSON hợp lệ cực kỳ nghiêm ngặt như mẫu sau:
{{
  ""ExamTitle"": ""Bài kiểm tra: {topic}"",
  ""DurationMinutes"": 30,
  ""Questions"": [
    {{
      ""QuestionText"": ""Câu hỏi trắc nghiệm một đáp án đúng?"",
      ""QuestionType"": ""MultipleChoice"",
      ""Points"": 10,
      ""Options"": [
        {{ ""OptionText"": ""Đáp án A"", ""IsCorrect"": false }},
        {{ ""OptionText"": ""Đáp án B"", ""IsCorrect"": true }},
        {{ ""OptionText"": ""Đáp án C"", ""IsCorrect"": false }},
        {{ ""OptionText"": ""Đáp án D"", ""IsCorrect"": false }}
      ]
    }},
    {{
      ""QuestionText"": ""Câu hỏi trắc nghiệm nhiều đáp án đúng (ví dụ có 2 hoặc nhiều đáp án đúng)?"",
      ""QuestionType"": ""MultipleChoice"",
      ""Points"": 10,
      ""Options"": [
        {{ ""OptionText"": ""Đáp án A đúng"", ""IsCorrect"": true }},
        {{ ""OptionText"": ""Đáp án B sai"", ""IsCorrect"": false }},
        {{ ""OptionText"": ""Đáp án C đúng"", ""IsCorrect"": true }},
        {{ ""OptionText"": ""Đáp án D sai"", ""IsCorrect"": false }}
      ]
    }},
    {{
      ""QuestionText"": ""Câu hỏi điền vào chỗ trống. Điền vào từ còn thiếu trong câu: 'Trái Đất quay quanh ______'?"",
      ""QuestionType"": ""FillInTheBlank"",
      ""Points"": 10,
      ""Options"": [
        {{ ""OptionText"": ""Mặt Trời"", ""IsCorrect"": true }}
      ]
    }},
    {{
      ""QuestionText"": ""Câu hỏi tự luận yêu cầu trình bày chi tiết?"",
      ""QuestionType"": ""Essay"",
      ""Points"": 10,
      ""Options"": []
    }}
  ]
}}
Lưu ý: 
- Đối với Trắc nghiệm (MultipleChoice), đánh dấu các đáp án đúng bằng ""IsCorrect"": true, và các đáp án sai bằng ""IsCorrect"": false. Cho phép một câu có nhiều đáp án đúng.
- Đối với Điền khuyết (FillInTheBlank), mảng Options chỉ chứa 1 phần tử duy nhất chính là đáp án chính xác của ô điền khuyết với ""IsCorrect"": true.
- Đối với Tự luận (Essay), mảng Options để rỗng [].
- Không dùng markdown.";

            var requestBody = new
            {
                contents = new[] { new { parts = new[] { new { text = prompt } } } }
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content);
            
            if (!response.IsSuccessStatusCode) return GetMockGeneratedQuiz(topic);

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);
            var textResult = doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();

            if (textResult == null) return GetMockGeneratedQuiz(topic);
            var cleanJson = ExtractJson(textResult);
            
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<GeneratedQuizDto>(cleanJson, options) ?? GetMockGeneratedQuiz(topic);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error in GenerateQuizAI: {ex.Message}");
            return GetMockGeneratedQuiz(topic);
        }
    }

    public async Task<GeneratedQuizDto> GenerateQuizFromDocumentAsync(string base64Data, string mimeType)
    {
        bool isExtractedText = false;
        string extractedText = "";
        var apiKey = _config["GeminiAI:ApiKey"];
        var model = _config["GeminiAI:Model"] ?? "gemini-1.5-flash";

        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("Gemini API Key missing. Returning Mock Quiz for Document.");
            return GetMockGeneratedQuiz("Tài liệu tải lên");
        }

        try
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
            
            var prompt = $@"Bạn là một Chuyên gia khảo thí giáo dục chuyên nghiệp.
Nhiệm vụ: Phân tích nội dung tài liệu đính kèm để trích xuất hoặc soạn thảo một bộ câu hỏi kiểm tra đầy đủ.

QUY TẮC QUAN TRỌNG:
1. Nếu tài liệu đính kèm đã có sẵn các câu hỏi (ví dụ: các dòng bắt đầu bằng 'Câu 1:', 'Câu 2:', 'Câu hỏi 1', hoặc dạng bảng danh sách câu hỏi), bạn BẮT BUỘC phải trích xuất ĐẦY ĐỦ 100% tất cả các câu hỏi đó, KHÔNG ĐƯỢC bỏ sót bất kỳ câu hỏi nào.
   - Nhận diện đúng loại câu hỏi dựa trên nội dung câu hỏi đó: Trắc nghiệm một/nhiều đáp án đúng (MultipleChoice), Điền khuyết (FillInTheBlank) hoặc Tự luận (Essay).
   - Nhận diện đúng các đáp án đúng và đáp án sai từ file. Ví dụ: đáp án có đánh dấu '[x]' hoặc ghi '(Đúng)' hoặc có cột 'Đáp án đúng' chỉ ra đáp án đúng.
2. Nếu tài liệu là văn bản kiến thức thông thường (không có sẵn danh sách câu hỏi), bạn hãy tự soạn một bộ đề thi tối đa 10 câu hỏi bao gồm đầy đủ các loại câu hỏi (MultipleChoice, FillInTheBlank, Essay) dựa trên nội dung tài liệu.

Yêu cầu trả về kết quả 100% dưới định dạng JSON hợp lệ cực kỳ nghiêm ngặt như mẫu sau:
{{
  ""ExamTitle"": ""Bài kiểm tra từ tài liệu"",
  ""DurationMinutes"": 30,
  ""Questions"": [
    {{
      ""QuestionText"": ""Câu hỏi trắc nghiệm một đáp án đúng?"",
      ""QuestionType"": ""MultipleChoice"",
      ""Points"": 10,
      ""Options"": [
        {{ ""OptionText"": ""Đáp án A"", ""IsCorrect"": false }},
        {{ ""OptionText"": ""Đáp án B"", ""IsCorrect"": true }},
        {{ ""OptionText"": ""Đáp án C"", ""IsCorrect"": false }},
        {{ ""OptionText"": ""Đáp án D"", ""IsCorrect"": false }}
      ]
    }},
    {{
      ""QuestionText"": ""Câu hỏi trắc nghiệm nhiều đáp án đúng (ví dụ có 2 hoặc nhiều đáp án đúng)?"",
      ""QuestionType"": ""MultipleChoice"",
      ""Points"": 10,
      ""Options"": [
        {{ ""OptionText"": ""Đáp án A đúng"", ""IsCorrect"": true }},
        {{ ""OptionText"": ""Đáp án B sai"", ""IsCorrect"": false }},
        {{ ""OptionText"": ""Đáp án C đúng"", ""IsCorrect"": true }},
        {{ ""OptionText"": ""Đáp án D sai"", ""IsCorrect"": false }}
      ]
    }},
    {{
      ""QuestionText"": ""Câu hỏi điền vào chỗ trống. Điền vào từ còn thiếu trong câu: 'Trái Đất quay quanh ______'?"",
      ""QuestionType"": ""FillInTheBlank"",
      ""Points"": 10,
      ""Options"": [
        {{ ""OptionText"": ""Mặt Trời"", ""IsCorrect"": true }}
      ]
    }},
    {{
      ""QuestionText"": ""Câu hỏi tự luận yêu cầu trình bày chi tiết?"",
      ""QuestionType"": ""Essay"",
      ""Points"": 10,
      ""Options"": []
    }}
  ]
}}
Lưu ý: 
- Đối với Trắc nghiệm (MultipleChoice), đánh dấu các đáp án đúng bằng ""IsCorrect"": true, và các đáp án sai bằng ""IsCorrect"": false. Cho phép một câu có nhiều đáp án đúng.
- Đối với Điền khuyết (FillInTheBlank), mảng Options chỉ chứa 1 phần tử duy nhất chính là đáp án chính xác của ô điền khuyết với ""IsCorrect"": true.
- Đối với Tự luận (Essay), mảng Options để rỗng [].
- Không dùng markdown.";

            isExtractedText = false;
            extractedText = "";
            byte[] fileBytes = Convert.FromBase64String(base64Data);

            // 1. First try to extract as a Word (.docx) document
            try
            {
                using (var ms = new MemoryStream(fileBytes))
                {
                    using (var docxDoc = DocX.Load(ms))
                    {
                        var sb = new StringBuilder();
                        foreach (var p in docxDoc.Paragraphs)
                        {
                            var t = p.Text.Trim();
                            if (!string.IsNullOrEmpty(t)) sb.AppendLine(t);
                        }
                        foreach (var table in docxDoc.Tables)
                        {
                            foreach (var row in table.Rows)
                            {
                                var cellTexts = new List<string>();
                                foreach (var cell in row.Cells)
                                {
                                    var cellSb = new StringBuilder();
                                    foreach (var p in cell.Paragraphs)
                                    {
                                        var pt = p.Text.Trim();
                                        if (!string.IsNullOrEmpty(pt)) cellSb.Append(pt + " ");
                                    }
                                    var cellT = cellSb.ToString().Trim();
                                    if (!string.IsNullOrEmpty(cellT)) cellTexts.Add(cellT);
                                }
                                if (cellTexts.Any()) sb.AppendLine(string.Join(" | ", cellTexts));
                            }
                        }
                        extractedText = sb.ToString();
                        isExtractedText = true;
                        _logger.LogInformation("Successfully extracted text as a Word document.");
                    }
                }
            }
            catch (Exception ex)
            {
                // Word parsing failed, which is expected if it's not a Word document. Log only if mimeType explicitly said it's Word.
                if (mimeType.Contains("word") || mimeType.Contains("docx") || mimeType.Contains("officedocument.wordprocessingml"))
                {
                    _logger.LogError($"Failed to extract Word text (MimeType was {mimeType}): {ex.Message}");
                }
            }

            // 2. If it's not Word, try to extract as an Excel (.xlsx) spreadsheet
            if (!isExtractedText)
            {
                try
                {
                    using (var ms = new MemoryStream(fileBytes))
                    {
                        using (var workbook = new XLWorkbook(ms))
                        {
                            var sb = new StringBuilder();
                            foreach (var sheet in workbook.Worksheets)
                            {
                                sb.AppendLine($"--- Worksheet: {sheet.Name} ---");
                                var lastRow = sheet.LastRowUsed();
                                if (lastRow == null) continue;
                                int rowCount = lastRow.RowNumber();
                                for (int r = 1; r <= rowCount; r++)
                                {
                                    var row = sheet.Row(r);
                                    var cells = new List<string>();
                                    var lastCell = row.LastCellUsed();
                                    if (lastCell == null) continue;
                                    int colCount = lastCell.Address.ColumnNumber;
                                    for (int c = 1; c <= colCount; c++)
                                    {
                                        cells.Add(row.Cell(c).Value.ToString() ?? "");
                                    }
                                    sb.AppendLine(string.Join(" | ", cells));
                                }
                            }
                            extractedText = sb.ToString();
                            isExtractedText = true;
                            _logger.LogInformation("Successfully extracted text as an Excel spreadsheet.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Excel parsing failed, which is expected if it's not an Excel document. Log only if mimeType explicitly said it's Excel.
                    if (mimeType.Contains("sheet") || mimeType.Contains("excel") || mimeType.Contains("officedocument.spreadsheetml"))
                    {
                        _logger.LogError($"Failed to extract Excel text (MimeType was {mimeType}): {ex.Message}");
                    }
                }
            }

            object requestBody;
            if (isExtractedText)
            {
                prompt += $"\n\nNỘI DUNG TÀI LIỆU DƯỚI ĐÂY, HÃY DÙNG NỘI DUNG NÀY ĐỂ SOẠN CÂU HỎI:\n{extractedText}";
                requestBody = new
                {
                    contents = new[] 
                    { 
                        new { 
                            parts = new object[] 
                            { 
                                new { text = prompt }
                            } 
                        } 
                    }
                };
            }
            else
            {
                requestBody = new
                {
                    contents = new[] 
                    { 
                        new { 
                            parts = new object[] 
                            { 
                                new { text = prompt },
                                new { inlineData = new { mimeType = mimeType, data = base64Data } }
                            } 
                        } 
                    }
                };
            }

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content);
            
            if (!response.IsSuccessStatusCode) 
            {
                var err = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Gemini File API Error: {err}");
                if (isExtractedText && !string.IsNullOrEmpty(extractedText))
                {
                    _logger.LogInformation("Using Fallback Local Quiz Parser due to API error.");
                    return ParseQuizFromText(extractedText);
                }
                return GetMockGeneratedQuiz("Tài liệu");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);
            var textResult = doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();

            if (textResult == null)
            {
                if (isExtractedText && !string.IsNullOrEmpty(extractedText))
                {
                    _logger.LogInformation("Using Fallback Local Quiz Parser due to null text result.");
                    return ParseQuizFromText(extractedText);
                }
                return GetMockGeneratedQuiz("Tài liệu");
            }
            var cleanJson = ExtractJson(textResult);
            
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var deserialized = JsonSerializer.Deserialize<GeneratedQuizDto>(cleanJson, options);
            if (deserialized != null) return deserialized;

            if (isExtractedText && !string.IsNullOrEmpty(extractedText))
            {
                _logger.LogInformation("Using Fallback Local Quiz Parser due to JSON deserialize failure.");
                return ParseQuizFromText(extractedText);
            }
            return GetMockGeneratedQuiz("Tài liệu");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error in GenerateQuizFromDocumentAsync: {ex.Message}");
            if (isExtractedText && !string.IsNullOrEmpty(extractedText))
            {
                _logger.LogInformation("Using Fallback Local Quiz Parser due to exception.");
                return ParseQuizFromText(extractedText);
            }
            return GetMockGeneratedQuiz("Tài liệu");
        }
    }

    private GeneratedQuizDto ParseQuizFromText(string text)
    {
        var result = new GeneratedQuizDto
        {
            ExamTitle = "Bài kiểm tra từ tài liệu (Local Parser)",
            DurationMinutes = 30,
            Questions = new List<GeneratedQuestionDto>()
        };

        if (string.IsNullOrWhiteSpace(text)) return result;

        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(l => l.Trim())
                        .Where(l => !string.IsNullOrEmpty(l))
                        .ToList();

        GeneratedQuestionDto? currentQuestion = null;

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];

            // Nhận diện câu hỏi mới: Câu 1, Câu 2...
            var match = Regex.Match(line, @"^(Câu\s+\d+|Câu hỏi\s+\d+|Question\s+\d+)[:\-\s\.]", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                if (currentQuestion != null)
                {
                    result.Questions.Add(currentQuestion);
                }

                currentQuestion = new GeneratedQuestionDto
                {
                    QuestionText = line,
                    QuestionType = "MultipleChoice", // Mặc định
                    Points = 10,
                    Options = new List<GeneratedOptionDto>()
                };

                // Nhận diện loại câu hỏi dựa trên tiêu đề
                var lowerLine = line.ToLower();
                if (lowerLine.Contains("điền") || lowerLine.Contains("blank") || lowerLine.Contains("chỗ trống"))
                {
                    currentQuestion.QuestionType = "FillInTheBlank";
                }
                else if (lowerLine.Contains("tự luận") || lowerLine.Contains("essay"))
                {
                    currentQuestion.QuestionType = "Essay";
                }
                continue;
            }

            if (currentQuestion != null)
            {
                var lowerLine = line.ToLower();

                if (currentQuestion.QuestionType == "MultipleChoice")
                {
                    // Nhận diện các lựa chọn đáp án: A. B. C. D. hoặc [x] A. hoặc A) B)
                    var optMatch = Regex.Match(line, @"^(\[x\]\s+)?([A-Z])[\.\)\s]", RegexOptions.IgnoreCase);
                    if (optMatch.Success)
                    {
                        bool isCorrect = line.StartsWith("[x]", StringComparison.OrdinalIgnoreCase) || 
                                         lowerLine.Contains("(đúng)") || 
                                         lowerLine.Contains("(đáp án đúng)") || 
                                         line.Contains("*");

                        // Làm sạch text đáp án
                        var cleanOptText = Regex.Replace(line, @"^(\[x\]\s+)?([A-Z])[\.\)\s]+", "", RegexOptions.IgnoreCase).Trim();
                        cleanOptText = Regex.Replace(cleanOptText, @"\((đúng|đáp án đúng|sai)\)", "", RegexOptions.IgnoreCase).Trim();
                        cleanOptText = cleanOptText.TrimEnd('*').Trim();

                        currentQuestion.Options.Add(new GeneratedOptionDto
                        {
                            OptionText = cleanOptText,
                            IsCorrect = isCorrect
                        });
                    }
                    else if (lowerLine.StartsWith("đáp án đúng:") || lowerLine.StartsWith("đáp án:"))
                    {
                        currentQuestion.QuestionType = "FillInTheBlank";
                        var ans = Regex.Replace(line, @"^(đáp án đúng|đáp án):\s*", "", RegexOptions.IgnoreCase).Trim();
                        currentQuestion.Options.Clear();
                        currentQuestion.Options.Add(new GeneratedOptionDto { OptionText = ans, IsCorrect = true });
                    }
                    else
                    {
                        currentQuestion.QuestionText += "\n" + line;
                    }
                }
                else if (currentQuestion.QuestionType == "FillInTheBlank")
                {
                    if (lowerLine.StartsWith("đáp án đúng:") || lowerLine.StartsWith("đáp án:"))
                    {
                        var ans = Regex.Replace(line, @"^(đáp án đúng|đáp án):\s*", "", RegexOptions.IgnoreCase).Trim();
                        currentQuestion.Options.Clear();
                        currentQuestion.Options.Add(new GeneratedOptionDto { OptionText = ans, IsCorrect = true });
                    }
                    else
                    {
                        currentQuestion.QuestionText += "\n" + line;
                    }
                }
                else if (currentQuestion.QuestionType == "Essay")
                {
                    currentQuestion.QuestionText += "\n" + line;
                }
            }
        }

        if (currentQuestion != null)
        {
            result.Questions.Add(currentQuestion);
        }

        // Tự động gán loại câu hỏi nếu là MultipleChoice nhưng không có Options
        foreach (var q in result.Questions)
        {
            if (q.QuestionType == "MultipleChoice" && (q.Options == null || q.Options.Count == 0))
            {
                q.QuestionType = "Essay";
            }
        }

        return result;
    }

    public async Task<GeneratedModuleDto> GenerateModuleAsync(string topic)
    {
        var apiKey = _config["GeminiAI:ApiKey"];
        var model = _config["GeminiAI:Model"] ?? "gemini-1.5-flash";

        if (string.IsNullOrEmpty(apiKey))
        {
            return new GeneratedModuleDto { Title = $"Chương: {topic} (Draft AI)", LessonTitles = new List<string> { "Bài 1: Tổng quan", "Bài 2: Chi tiết" } };
        }

        try
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
            var prompt = $@"Nhiệm vụ: Đề xuất nội dung cho 1 chương (module) trong khóa học về: '{topic}'.
Yêu cầu: Trả về 100% định dạng JSON chính xác như sau:
{{
  ""Title"": ""Tên chương hấp dẫn"",
  ""LessonTitles"": [""Bài 1: ..."", ""Bài 2: ..."", ""Bài 3: ...""]
}}
Chỉ trả về JSON, không markdown.";

            var requestBody = new { contents = new[] { new { parts = new[] { new { text = prompt } } } } };
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content);
            if (!response.IsSuccessStatusCode) return new GeneratedModuleDto { Title = topic, LessonTitles = new List<string>() };

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);
            var textResult = doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
            if (textResult == null) return new GeneratedModuleDto { Title = topic, LessonTitles = new List<string>() };
            var cleanJson = ExtractJson(textResult);
            return JsonSerializer.Deserialize<GeneratedModuleDto>(cleanJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new GeneratedModuleDto { Title = topic };
        }
        catch { return new GeneratedModuleDto { Title = topic }; }
    }

    public async Task<GeneratedLessonDto> GenerateLessonAsync(string topic)
    {
        var apiKey = _config["GeminiAI:ApiKey"];
        var model = _config["GeminiAI:Model"] ?? "gemini-1.5-flash";

        if (string.IsNullOrEmpty(apiKey))
        {
            return new GeneratedLessonDto { Title = $"Bài học: {topic}", ContentBody = $"Nội dung chi tiết về {topic} sẽ được cập nhật ở đây." };
        }

        try
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
            var prompt = $@"Nhiệm vụ: Soạn thảo nội dung chi tiết cho 1 bài học về: '{topic}'.
Yêu cầu: Trả về 100% định dạng JSON chính xác như sau:
{{
  ""Title"": ""Tên bài học"",
  ""ContentBody"": ""Toàn bộ nội dung bài học dưới định dạng HTML đơn giản (dùng p, b, ul, li). Trình bày khoa học, chuyên nghiệp.""
}}
Chỉ trả về JSON, không markdown.";

            var requestBody = new { contents = new[] { new { parts = new[] { new { text = prompt } } } } };
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content);
            if (!response.IsSuccessStatusCode) return new GeneratedLessonDto { Title = topic, ContentBody = "Đang soạn thảo..." };

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);
            var textResult = doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
            if (textResult == null) return new GeneratedLessonDto { Title = topic, ContentBody = "" };
            var cleanJson = ExtractJson(textResult);
            return JsonSerializer.Deserialize<GeneratedLessonDto>(cleanJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new GeneratedLessonDto { Title = topic };
        }
        catch { return new GeneratedLessonDto { Title = topic }; }
    }

    private GeneratedQuizDto GetMockGeneratedQuiz(string topic)
    {
        return new GeneratedQuizDto
        {
            ExamTitle = $"Bài Kiểm Tra: {topic} (Bản thảo AI)",
            DurationMinutes = 30,
            Questions = new List<GeneratedQuestionDto>
            {
                new GeneratedQuestionDto {
                    QuestionText = $"Đâu là khái niệm cơ bản nhất của {topic}?",
                    QuestionType = "MultipleChoice",
                    Options = new List<GeneratedOptionDto> {
                        new GeneratedOptionDto { OptionText = "Khái niệm A", IsCorrect = false },
                        new GeneratedOptionDto { OptionText = "Khái niệm B (Đúng)", IsCorrect = true },
                        new GeneratedOptionDto { OptionText = "Khái niệm C", IsCorrect = false },
                        new GeneratedOptionDto { OptionText = "Khái niệm D", IsCorrect = false }
                    },
                    Points = 10
                },
                new GeneratedQuestionDto {
                    QuestionText = $"Điền vào chỗ trống: '{topic} là một phương pháp ______ hiệu quả.'",
                    QuestionType = "FillInTheBlank",
                    Options = new List<GeneratedOptionDto> {
                        new GeneratedOptionDto { OptionText = "quản lý", IsCorrect = true }
                    },
                    Points = 10
                },
                new GeneratedQuestionDto {
                    QuestionText = $"Hãy trình bày quan điểm cá nhân của bạn về lợi ích thực tiễn của {topic}.",
                    QuestionType = "Essay",
                    Options = new List<GeneratedOptionDto>(),
                    Points = 10
                }
            }
        };
    }

    public async Task<string> AnswerStudentQuestionAsync(string courseTitle, string lessonContext, string studentQuestion)
    {
        var apiKey = _config["GeminiAI:ApiKey"];
        var model = _config["GeminiAI:Model"] ?? "gemini-1.5-flash";

        if (string.IsNullOrEmpty(apiKey))
        {
            return "Xin chào! Hiện tại tính năng Trợ lý AI đang được bảo trì (Vui lòng nhập API Key vào appsettings.json). Tôi luôn sẵn sàng hỗ trợ bạn ngay khi kết nối được phục hồi.";
        }

        try
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
            
            var prompt = $@"Bạn là Giảng viên AI nhiệt tình và uyên bác cho Khóa học '{courseTitle}'.
Bối cảnh bài học hiện tại: '{lessonContext}'
Câu hỏi của học viên: '{studentQuestion}'

Nhiệm vụ: Hãy trả lời học viên một cách súc tích, dễ hiểu, chuyên nghiệp và có tính khích lệ học tập. Trả về dưới định dạng HTML đơn giản (như dùng <b>, <p>, <ul>) để hiển thị trên web. Nếu hỏi không liên quan khóa học, hãy khéo léo nhắc nhở quay lại.";

            var requestBody = new
            {
                contents = new[] { new { parts = new[] { new { text = prompt } } } }
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content);
            
            if (!response.IsSuccessStatusCode)
                return "Rất tiếc, tôi đang gặp chút sự cố kết nối API. Bạn có thể thử lại sau được không?";

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);
            var textResult = doc.RootElement
                                .GetProperty("candidates")[0]
                                .GetProperty("content")
                                .GetProperty("parts")[0]
                                .GetProperty("text")
                                .GetString();

            return textResult ?? "Không có câu trả lời.";
        }
        catch (Exception ex)
        {
            _logger.LogError($"AI Assistant Error: {ex.Message}");
            return "Có lỗi kỹ thuật, AI chưa thể trả lời bạn lúc này.";
        }
    }

    public async Task<string> SummarizeModuleAsync(string moduleTitle, string lessonsContext)
    {
        var apiKey = _config["GeminiAI:ApiKey"];
        var model = _config["GeminiAI:Model"] ?? "gemini-1.5-flash";

        if (string.IsNullOrEmpty(apiKey))
        {
            return "Xin chào! Hiện tại tính năng Tóm tắt AI đang được bảo trì. Vui lòng quay lại sau.";
        }

        try
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
            
            var prompt = $@"Bạn là Chuyên gia Đào tạo cao cấp.
Nhiệm vụ: Hãy tóm tắt nội dung của chương học '{moduleTitle}' dựa trên danh sách bài học và nội dung bài học dưới đây.

Nội dung tham khảo:
{lessonsContext}

Yêu cầu: 
1. Bản tóm tắt phải súc tích, làm nổi bật được các kiến thức cốt lõi.
2. Trình bày dưới định dạng HTML đơn giản (dùng <b>, <p>, <ul>, <li>).
3. Sử dụng ngôn từ chuyên nghiệp, dễ hiểu và truyền cảm hứng học tập.";

            var requestBody = new
            {
                contents = new[] { new { parts = new[] { new { text = prompt } } } }
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content);
            
            if (!response.IsSuccessStatusCode)
                return "Rất tiếc, AI chưa thể tóm tắt nội dung này lúc này. Vui lòng thử lại sau.";

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);
            var textResult = doc.RootElement
                                .GetProperty("candidates")[0]
                                .GetProperty("content")
                                .GetProperty("parts")[0]
                                .GetProperty("text")
                                .GetString();

            return textResult ?? "Không thể tạo bản tóm tắt.";
        }
        catch (Exception ex)
        {
            _logger.LogError($"AI Module Summary Error: {ex.Message}");
            return "Có lỗi kỹ thuật khi thực hiện tóm tắt nội dung chương học.";
        }
    }

    private GeneratedCourseDto GetMockGeneratedCourse(string topic)
    {
        return new GeneratedCourseDto
        {
            Title = $"Khóa học: Nâng Cấp Kỹ Năng {topic}",
            Description = $"Khóa học này cung cấp quy trình toàn diện nhằm làm chủ {topic}. Nội dung do AI tự động biên soạn (Mock Mode do thiếu API Key).\n\nLợi ích:\n✅ Hiểu sâu về cách thức ứng dụng.\n✅ Case study thực tế phù hợp với chuyên môn của phòng ban.\n✅ Nâng cao hiệu suất xử lý công việc ngay sau khóa học.",
            Modules = new List<GeneratedModuleDto>
            {
                new GeneratedModuleDto
                {
                    Title = "Chương 1: Tổng quan và Lợi ích",
                    LessonTitles = new List<string> { "Bài 1: Giới thiệu chung", "Bài 2: Tính ứng dụng thực tiễn" }
                },
                new GeneratedModuleDto
                {
                    Title = "Chương 2: Kiến thức Chuyên sâu",
                    LessonTitles = new List<string> { "Bài 1: Quy trình thực hiện", "Bài 2: Bài tập tình huống (Case study)", "Bài 3: Đánh giá quá trình" }
                }
            }
        };
    }

    public async Task<GeneratedCourseWithLessonsDto> GenerateCourseFromWordTextAsync(string wordText)
    {
        _logger.LogInformation($"[AI Service] Nhận yêu cầu tạo khóa học từ file Word. Độ dài văn bản: {wordText?.Length ?? 0} ký tự.");
        Console.WriteLine($"[AI Service] Nhận yêu cầu tạo khóa học từ file Word. Độ dài văn bản: {wordText?.Length ?? 0} ký tự.");

        var apiKey = _config["GeminiAI:ApiKey"];
        var model = _config["GeminiAI:Model"] ?? "gemini-1.5-flash";

        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("Gemini API Key is missing. Using Fallback Word Parser.");
            return ParseCourseFromWordText(wordText);
        }

        try
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
            
            var prompt = $@"Bạn là Giám đốc Đào tạo ưu tú của một Tập đoàn lớn.
Nhiệm vụ: Hãy phân tích văn bản thô trích xuất từ file Word của khóa học và xây dựng cấu trúc khóa học hoàn chỉnh bao gồm các chương và bài học với nội dung bài học chi tiết.
Văn bản thô từ tài liệu khóa học:
{wordText}

Yêu cầu phân tích và trích xuất:
1. Xác định Tiêu đề khóa học (Title) và Mô tả khóa học (Description) ở phần đầu tài liệu.
2. Trích xuất các Chương (Modules). Một chương sẽ chứa tiêu đề chương (ví dụ: 'Chương 1: [Tiêu đề]') và danh sách các Bài học (Lessons) thuộc chương đó.
3. Với mỗi Bài học, trích xuất Tiêu đề bài học (Title) và toàn bộ Nội dung bài học (ContentBody). Nội dung bài học phải được định dạng HTML đơn giản (sử dụng p, b, strong, ul, li) để trình bày rõ ràng, khoa học và giữ lại tất cả kiến thức chuyên sâu từ tài liệu gốc. Không để trống nội dung bài học.

Vui lòng trả về kết quả 100% dưới định dạng JSON hợp lệ cực kỳ nghiêm ngặt như mẫu sau:
{{
  ""Title"": ""Tên khóa học"",
  ""Description"": ""Mô tả khóa học"",
  ""Modules"": [
    {{
      ""Title"": ""Chương ..."",
      ""Lessons"": [
        {{
          ""Title"": ""Bài ..."",
          ""ContentBody"": ""Nội dung bài học dạng HTML""
        }}
      ]
    }}
  ]
}}
Lưu ý: Chỉ trả về JSON thuần, không markdown, không bọc ```json.";

            var requestBody = new
            {
                contents = new[]
                {
                    new { parts = new[] { new { text = prompt } } }
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorText = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Gemini File API Error: {errorText}");
                return ParseCourseFromWordText(wordText);
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);
            
            var textResult = doc.RootElement
                                .GetProperty("candidates")[0]
                                .GetProperty("content")
                                .GetProperty("parts")[0]
                                .GetProperty("text")
                                .GetString();

            if (textResult == null) return ParseCourseFromWordText(wordText);

            var cleanJson = ExtractJson(textResult);
            
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<GeneratedCourseWithLessonsDto>(cleanJson, options);
            
            return result ?? ParseCourseFromWordText(wordText);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error in GenerateCourseFromWordTextAsync: {ex.Message}");
            return ParseCourseFromWordText(wordText);
        }
    }

    public async Task<GeneratedQuizDto> GenerateQuizFromLessonsAsync(string moduleTitle, string lessonsContent)
    {
        var apiKey = _config["GeminiAI:ApiKey"];
        var model = _config["GeminiAI:Model"] ?? "gemini-1.5-flash";

        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("Gemini API Key is missing. Using Mock Mode for GenerateQuizFromLessonsAsync.");
            return GetMockGeneratedQuiz(moduleTitle);
        }

        try
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
            
            var prompt = $@"Bạn là một Chuyên gia khảo thí giáo dục chuyên nghiệp.
Nhiệm vụ: Hãy phân tích nội dung của chương học '{moduleTitle}' dưới đây và xây dựng một bộ đề kiểm tra trắc nghiệm tối đa 10 câu hỏi dựa trên nội dung đó.
Đề thi cần kiểm tra các kiến thức cốt lõi. Có 3 loại câu hỏi: Trắc nghiệm (MultipleChoice - có thể chọn 1 hoặc nhiều đáp án đúng), Điền vào chỗ trống (FillInTheBlank - có 1 ô nhập đáp án đúng), Tự luận (Essay - bài tự luận chấm tay). Hãy phân bổ hợp lý các loại câu hỏi (ưu tiên Trắc nghiệm và Điền khuyết).

Nội dung chương học:
{lessonsContent}

Yêu cầu trả về kết quả 100% dưới định dạng JSON hợp lệ cực kỳ nghiêm ngặt như mẫu sau:
{{
  ""ExamTitle"": ""Bài kiểm tra: {moduleTitle}"",
  ""DurationMinutes"": 30,
  ""Questions"": [
    {{
      ""QuestionText"": ""Câu hỏi trắc nghiệm một đáp án đúng?"",
      ""QuestionType"": ""MultipleChoice"",
      ""Points"": 10,
      ""Options"": [
        {{ ""OptionText"": ""Đáp án A"", ""IsCorrect"": false }},
        {{ ""OptionText"": ""Đáp án B"", ""IsCorrect"": true }},
        {{ ""OptionText"": ""Đáp án C"", ""IsCorrect"": false }},
        {{ ""OptionText"": ""Đáp án D"", ""IsCorrect"": false }}
      ]
    }},
    {{
      ""QuestionText"": ""Câu hỏi trắc nghiệm nhiều đáp án đúng (ví dụ có 2 hoặc nhiều đáp án đúng)?"",
      ""QuestionType"": ""MultipleChoice"",
      ""Points"": 10,
      ""Options"": [
        {{ ""OptionText"": ""Đáp án A đúng"", ""IsCorrect"": true }},
        {{ ""OptionText"": ""Đáp án B sai"", ""IsCorrect"": false }},
        {{ ""OptionText"": ""Đáp án C đúng"", ""IsCorrect"": true }},
        {{ ""OptionText"": ""Đáp án D sai"", ""IsCorrect"": false }}
      ]
    }},
    {{
      ""QuestionText"": ""Câu hỏi điền vào chỗ trống. Điền vào từ còn thiếu trong câu: 'Trái Đất quay quanh ______'?"",
      ""QuestionType"": ""FillInTheBlank"",
      ""Points"": 10,
      ""Options"": [
        {{ ""OptionText"": ""Mặt Trời"", ""IsCorrect"": true }}
      ]
    }},
    {{
      ""QuestionText"": ""Câu hỏi tự luận yêu cầu trình bày chi tiết?"",
      ""QuestionType"": ""Essay"",
      ""Points"": 10,
      ""Options"": []
    }}
  ]
}}
Lưu ý: 
- Đối với Trắc nghiệm (MultipleChoice), đánh dấu các đáp án đúng bằng ""IsCorrect"": true, và các đáp án sai bằng ""IsCorrect"": false. Cho phép một câu có nhiều đáp án đúng.
- Đối với Điền khuyết (FillInTheBlank), mảng Options chỉ chứa 1 phần tử duy nhất chính là đáp án chính xác của ô điền khuyết với ""IsCorrect"": true.
- Đối với Tự luận (Essay), mảng Options để rỗng [].
- Không dùng markdown.";

            var requestBody = new
            {
                contents = new[] { new { parts = new[] { new { text = prompt } } } }
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content);
            
            if (!response.IsSuccessStatusCode) return GetMockGeneratedQuiz(moduleTitle);

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);
            var textResult = doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();

            if (textResult == null) return GetMockGeneratedQuiz(moduleTitle);
            var cleanJson = ExtractJson(textResult);
            
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<GeneratedQuizDto>(cleanJson, options) ?? GetMockGeneratedQuiz(moduleTitle);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error in GenerateQuizFromLessonsAsync: {ex.Message}");
            return GetMockGeneratedQuiz(moduleTitle);
        }
    }

    private GeneratedCourseWithLessonsDto ParseCourseFromWordText(string? wordText)
    {
        _logger.LogInformation("[Fallback Parser] Bắt đầu tự động phân tích file Word bằng code dự phòng.");
        Console.WriteLine("[Fallback Parser] Bắt đầu tự động phân tích file Word bằng code dự phòng.");

        var course = new GeneratedCourseWithLessonsDto();
        if (string.IsNullOrWhiteSpace(wordText))
        {
            return GetMockGeneratedCourseWithLessons();
        }

        var lines = wordText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None)
                            .Select(l => l.Trim())
                            .Where(l => !string.IsNullOrEmpty(l))
                            .ToList();

        if (lines.Count == 0)
        {
            return GetMockGeneratedCourseWithLessons();
        }

        // Step 1: Detect Title and Description
        int startIndex = 0;
        string title = "";
        
        for (int i = 0; i < Math.Min(lines.Count, 5); i++)
        {
            var line = lines[i];
            if (line.StartsWith("Khóa học:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Tên khóa học:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Title:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Tên khoá học:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Tên khóa học", StringComparison.OrdinalIgnoreCase))
            {
                var idx = line.IndexOf(':');
                if (idx >= 0)
                {
                    title = line.Substring(idx + 1).Trim();
                }
                else
                {
                    title = line.Replace("Tên khóa học", "", StringComparison.OrdinalIgnoreCase).Trim();
                }
                startIndex = i + 1;
                break;
            }
        }

        if (string.IsNullOrEmpty(title))
        {
            title = lines[0];
            startIndex = 1;
        }

        course.Title = title;

        var descBuilder = new StringBuilder();
        int descEndIndex = startIndex;
        for (int i = startIndex; i < lines.Count; i++)
        {
            var line = lines[i];
            if (IsModuleLine(line))
            {
                descEndIndex = i;
                break;
            }
            if (line.StartsWith("Mô tả:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Mô tả khóa học:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Description:", StringComparison.OrdinalIgnoreCase))
            {
                var idx = line.IndexOf(':');
                descBuilder.AppendLine(idx >= 0 ? line.Substring(idx + 1).Trim() : line);
            }
            else
            {
                descBuilder.AppendLine(line);
            }
            descEndIndex = i + 1;
        }

        course.Description = descBuilder.ToString().Trim();

        // Step 2: Parse Modules and Lessons
        GeneratedModuleWithLessonsDto? currentModule = null;
        GeneratedLessonDto? currentLesson = null;
        var lessonBodyBuilder = new StringBuilder();

        for (int i = descEndIndex; i < lines.Count; i++)
        {
            var line = lines[i];
            if (IsModuleLine(line))
            {
                if (currentLesson != null)
                {
                    currentLesson.ContentBody = ConvertToHtml(lessonBodyBuilder.ToString());
                    lessonBodyBuilder.Clear();
                }

                currentModule = new GeneratedModuleWithLessonsDto
                {
                    Title = line,
                    Lessons = new List<GeneratedLessonDto>()
                };
                course.Modules.Add(currentModule);
                currentLesson = null;
            }
            else if (IsLessonLine(line))
            {
                if (currentLesson != null)
                {
                    currentLesson.ContentBody = ConvertToHtml(lessonBodyBuilder.ToString());
                    lessonBodyBuilder.Clear();
                }

                if (currentModule == null)
                {
                    currentModule = new GeneratedModuleWithLessonsDto
                    {
                        Title = "Chương 1: Tổng quan",
                        Lessons = new List<GeneratedLessonDto>()
                    };
                    course.Modules.Add(currentModule);
                }

                currentLesson = new GeneratedLessonDto
                {
                    Title = line,
                    ContentBody = ""
                };
                currentModule.Lessons.Add(currentLesson);
            }
            else
            {
                if (currentLesson != null)
                {
                    lessonBodyBuilder.AppendLine(line);
                }
                else
                {
                    course.Description += "\n" + line;
                }
            }
        }

        if (currentLesson != null)
        {
            currentLesson.ContentBody = ConvertToHtml(lessonBodyBuilder.ToString());
        }

        // If no modules/lessons were parsed because the document has no headers
        if (course.Modules.Count == 0)
        {
            var defaultModule = new GeneratedModuleWithLessonsDto
            {
                Title = "Chương 1: Nội dung chi tiết",
                Lessons = new List<GeneratedLessonDto>()
            };
            course.Modules.Add(defaultModule);

            int paragraphGroupSize = 4;
            int lessonNum = 1;
            for (int i = descEndIndex; i < lines.Count; i += paragraphGroupSize)
            {
                var chunk = lines.Skip(i).Take(paragraphGroupSize).ToList();
                if (chunk.Count > 0)
                {
                    var lessonTitle = chunk[0];
                    var contentLines = chunk.Skip(1).ToList();
                    if (contentLines.Count == 0)
                    {
                        contentLines.Add(lessonTitle);
                        lessonTitle = $"Bài {lessonNum}: Tiếp tục nội dung";
                    }
                    else if (!lessonTitle.StartsWith("Bài", StringComparison.OrdinalIgnoreCase))
                    {
                        lessonTitle = $"Bài {lessonNum}: {lessonTitle}";
                    }

                    defaultModule.Lessons.Add(new GeneratedLessonDto
                    {
                        Title = lessonTitle,
                        ContentBody = ConvertToHtml(string.Join("\n", contentLines))
                    });
                    lessonNum++;
                }
            }
        }

        _logger.LogInformation($"[Fallback Parser] Hoàn thành: Title='{course.Title}', Description='{course.Description}', ModulesCount={course.Modules.Count}");
        Console.WriteLine($"[Fallback Parser] Hoàn thành: Title='{course.Title}', Description='{course.Description}', ModulesCount={course.Modules.Count}");

        return course;
    }

    private GeneratedCourseWithLessonsDto GetMockGeneratedCourseWithLessons()
    {
        return new GeneratedCourseWithLessonsDto
        {
            Title = "Khóa học: Phương pháp Làm việc Hiệu quả",
            Description = "Khóa học này hướng dẫn các phương pháp quản lý thời gian, tối ưu hóa hiệu suất và làm việc nhóm dành cho nhân viên.",
            Modules = new List<GeneratedModuleWithLessonsDto>
            {
                new GeneratedModuleWithLessonsDto
                {
                    Title = "Chương 1: Kỹ năng Quản lý Thời gian",
                    Lessons = new List<GeneratedLessonDto>
                    {
                        new GeneratedLessonDto
                        {
                            Title = "Bài 1.1: Nguyên lý Pomodoro",
                            ContentBody = "<p>Phương pháp Pomodoro giúp tập trung cao độ bằng cách chia thời gian làm việc thành các khoảng nhỏ: <b>25 phút làm việc</b>, tiếp theo là <b>5 phút nghỉ ngơi</b>. Sau 4 chu kỳ như vậy, nghỉ dài từ 15-30 phút.</p>"
                        },
                        new GeneratedLessonDto
                        {
                            Title = "Bài 1.2: Ma trận Eisenhower",
                            ContentBody = "<p>Phân loại công việc thành 4 nhóm để ưu tiên xử lý: <b>Khẩn cấp & Quan trọng</b>, <b>Quan trọng nhưng Không khẩn cấp</b>, <b>Khẩn cấp nhưng Không quan trọng</b>, và <b>Không quan trọng & Không khẩn cấp</b>.</p>"
                        }
                    }
                },
                new GeneratedModuleWithLessonsDto
                {
                    Title = "Chương 2: Giao tiếp Hiệu quả trong Đội nhóm",
                    Lessons = new List<GeneratedLessonDto>
                    {
                        new GeneratedLessonDto
                        {
                            Title = "Bài 2.1: Quy tắc phản hồi 3C",
                            ContentBody = "<p>Phản hồi trong đội nhóm cần tuân thủ 3 nguyên tắc chính: <b>Clear</b> (Rõ ràng), <b>Constructive</b> (Mang tính xây dựng), và <b>Consistent</b> (Nhất quán) để xây dựng lòng tin và cải thiện chất lượng công việc.</p>"
                        }
                    }
                }
            }
        };
    }

    private bool IsModuleLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;
        return line.StartsWith("Chương", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("Phần", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("Module", StringComparison.OrdinalIgnoreCase) ||
               Regex.IsMatch(line, @"^[IVXLCDM]+\.", RegexOptions.IgnoreCase);
    }

    private bool IsLessonLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;
        return line.StartsWith("Bài", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("Lesson", StringComparison.OrdinalIgnoreCase) ||
               Regex.IsMatch(line, @"^\d+(\.\d+)+");
    }

    private string ConvertToHtml(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return "";
        var lines = rawText.Split('\n')
                           .Select(l => l.Trim())
                           .Where(l => !string.IsNullOrEmpty(l))
                           .ToList();

        var sb = new StringBuilder();
        bool inList = false;

        foreach (var line in lines)
        {
            if (line.StartsWith("-") || line.StartsWith("*") || line.StartsWith("+"))
            {
                if (!inList)
                {
                    sb.AppendLine("<ul>");
                    inList = true;
                }
                var clean = line.Substring(1).Trim();
                sb.AppendLine($"  <li>{clean}</li>");
            }
            else
            {
                if (inList)
                {
                    sb.AppendLine("</ul>");
                    inList = false;
                }
                sb.AppendLine($"<p>{line}</p>");
            }
        }

        if (inList)
        {
            sb.AppendLine("</ul>");
        }

        return sb.ToString();
    }

    private string ExtractJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var trimmed = text.Trim();
        
        if (trimmed.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring(7);
        }
        else if (trimmed.StartsWith("```", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring(3);
        }

        if (trimmed.EndsWith("```"))
        {
            trimmed = trimmed.Substring(0, trimmed.Length - 3);
        }

        trimmed = trimmed.Trim();

        int firstBrace = trimmed.IndexOf('{');
        int lastBrace = trimmed.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            return trimmed.Substring(firstBrace, lastBrace - firstBrace + 1);
        }
        return trimmed;
    }
}

