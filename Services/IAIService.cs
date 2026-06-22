namespace KhoaHoc.Services;

public class GeneratedCourseDto
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public List<GeneratedModuleDto> Modules { get; set; } = new();
}

public class GeneratedModuleDto
{
    public string Title { get; set; } = "";
    public List<string> LessonTitles { get; set; } = new();
}

public class GeneratedLessonDto
{
    public string Title { get; set; } = "";
    public string ContentBody { get; set; } = "";
}

public class GeneratedModuleWithLessonsDto
{
    public string Title { get; set; } = "";
    public List<GeneratedLessonDto> Lessons { get; set; } = new();
}

public class GeneratedCourseWithLessonsDto
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public List<GeneratedModuleWithLessonsDto> Modules { get; set; } = new();
}

public class GeneratedQuizDto
{
    public string ExamTitle { get; set; } = "";
    public int DurationMinutes { get; set; } = 30;
    public List<GeneratedQuestionDto> Questions { get; set; } = new();
}

public class GeneratedOptionDto
{
    public string OptionText { get; set; } = "";
    public bool IsCorrect { get; set; }
}

public class GeneratedQuestionDto
{
    public string QuestionText { get; set; } = "";
    public string QuestionType { get; set; } = "MultipleChoice"; // "MultipleChoice", "FillInTheBlank", "Essay"
    public int Points { get; set; } = 10;
    public List<GeneratedOptionDto> Options { get; set; } = new();
    public int CorrectOptionIndex { get; set; }
}

public interface IAIService
{
    Task<GeneratedCourseDto> GenerateCourseContentAsync(string topic);
    Task<GeneratedQuizDto> GenerateQuizAsync(string topic);
    Task<GeneratedQuizDto> GenerateQuizFromDocumentAsync(string base64Data, string mimeType);
    Task<GeneratedModuleDto> GenerateModuleAsync(string topic);
    Task<GeneratedLessonDto> GenerateLessonAsync(string topic);
    Task<string> AnswerStudentQuestionAsync(string courseTitle, string lessonContext, string studentQuestion);
    Task<string> SummarizeModuleAsync(string moduleTitle, string lessonsContext);
    Task<GeneratedCourseWithLessonsDto> GenerateCourseFromWordTextAsync(string wordText);
    Task<GeneratedQuizDto> GenerateQuizFromLessonsAsync(string moduleTitle, string lessonsContent);
}
