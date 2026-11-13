using System.Text.RegularExpressions;

namespace SC.ScriptForge.Services;

/// <summary>
/// Определяет задачу по тексту запроса без обращения к модели: мгновенно и предсказуемо.
/// Пользователь видит результат и может выбрать задачу вручную.
/// </summary>
public static class AiIntentRouter
{
    private static readonly RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private static Regex R(string pattern) => new(pattern, Options, TimeSpan.FromMilliseconds(100));

    private static readonly Regex ErrorDump = R(@"fullyqualifiederrorid|categoryinfo\s*:|at line:\s*\d|строка:\s*\d|знак:\s*\d|\+\s*~{2,}|exception calling|is not recognized|не распознано как имя|cannot be found|не удается найти|running scripts is disabled|выполнение сценариев отключено");
    private static readonly Regex ErrorWords = R(@"ошибк|ошибоч|\berror\b|не работает|не запускается|падает|вылетает|выда[её]т|exception|failed|не удается|не удаётся|access is denied|отказано в доступе");
    private static readonly Regex Syntax = R(@"синтакс|syntax|parser|парсер|не разбирается|не компилир|незакрыт|не закрыт|скобк|кавычк|unexpected token|missing\s+(closing|end)");
    private static readonly Regex FixVerb = R(@"\b(исправь|исправить|исправьте|почини|починить|поправь|поправить|устрани|устранить|fix|repair)\b");
    private static readonly Regex RetryFailure = R(@"не помогл|не исправ|не почини|та же ошибка|вс[её] ещ[её]|по-прежнему|still (fails|broken|not working)|didn.t (work|fix)");
    private static readonly Regex BareFix = R(@"^\s*(исправь|почини|поправь|fix|repair)(\s+(это|код|скрипт|it))?[.!]?\s*$");
    private static readonly Regex Handling =R(@"обработк\w*\s+(ошиб|исключ)|try\s*/?\s*catch|\btry\b|\bcatch\b|перехват");
    private static readonly Regex CreateVerb = R(@"\b(напиши|написать|создай|создать|сгенерируй|сгенерировать|сделай|сделать|нужен|нужна|нужно|хочу|подготовь|набросай|write|create|generate|make|build)\b");
    private static readonly Regex ScriptNoun = R(@"скрипт|сценари|script|программ|утилит|автоматизир|ps1");
    private static readonly Regex NewWord = R(@"\bнов(ый|ую|ое|ая)\b|с нуля|\bnew\b|from scratch|отдельн|другой скрипт");
    private static readonly Regex BasedOn = R(@"на основе|как этот|как в этом|похож|такой же|аналогичн|по образцу|based on|similar");
    private static readonly Regex Question = R(@"^\s*(как|что|почему|зачем|какой|какая|какие|каким|чем|где|когда|можно ли|можно|есть ли|сколько|в чём|в чем|how|what|why|which|where|when|can|is|does)\b|\?\s*$");
    private static readonly Regex Imperative = R(@"^\s*(удали|очисти|найди|выгрузи|получи|покажи|собери|проверь|перезапусти|останови|запусти|экспортируй|импортируй|скопируй|перенеси|архивируй|создай|включи|отключи|установи|обнови|сделай|отправь|переименуй|посчитай|выведи|сохрани|сравни|загрузи|скачай|настрой|добавь|мониторь|отслеживай|delete|remove|clean|find|get|list|export|copy|move|backup|restart|stop|start|install|update|set|send|rename|count|monitor)\b");
    private static readonly Regex Versions = R(@"верси|что изменилось|что поменялось|сравни\w*\s+(с|верси)|\bdiff\b|история изменений");
    private static readonly Regex TitleTags = R(@"\bтег|\bтэг|назван|переименуй|\btags?\b|\btitle\b");
    private static readonly Regex DescriptionNoun = R(@"описани|description|summary|аннотаци");
    private static readonly Regex Detailed = R(@"подробн|инструкц|документац|readme|руководств|справк");
    private static readonly Regex Comments = R(@"комментар|comment");
    private static readonly Regex Logging = R(@"логир|журналир|\bлог(и|ов|а)?\b|logging|\blogs?\b|write-log|transcript");
    private static readonly Regex Optimize = R(@"оптимиз|ускор|быстрее|производительн|медленн|optimi[sz]|faster|performance|refactor|рефактор");
    private static readonly Regex Audit = R(@"проблем|уязвим|безопасн|аудит|\bбаг|недоч[её]т|слаб\w+ мест|что не так|проверь|review|audit|bugs?\b|issues?\b|риск");
    private static readonly Regex Explain = R(@"объясни|поясни|что делает|как работает|разбер|расскажи|опиши,? что|что тут|что здесь|explain|what does");
    private static readonly Regex Edit = R(@"\b(допиши|добавь|измени|исправь|переделай|сделай|замени|убери|удали|перепиши|доработай|улучши|дополни|поменяй|расширь|пусть|чтобы|add|fix|change|modify|update|improve|remove|replace|rewrite|extend)\b");

    /// <summary>Пользователь просит новый скрипт, а не доработку текущего черновика.</summary>
    public static bool IsFreshRequest(string text) => NewWord.IsMatch(text ?? "");

    /// <summary>Возвращает Id задачи из <see cref="AiTaskDefinition.All"/>.</summary>
    public static string Detect(string text, bool hasScript, bool hasDraft)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) return hasScript ? "explain" : "ask";
        var question = Question.IsMatch(text);
        var errorDump = ErrorDump.IsMatch(text);
        var handling = Handling.IsMatch(text);
        var wantsNew = CreateVerb.IsMatch(text) && ScriptNoun.IsMatch(text) && !question;
        // Ошибка или просьба починить: при прикреплённом скрипте сразу исправляем код, а не только объясняем.
        var broken = !handling && (errorDump || Syntax.IsMatch(text) || ErrorWords.IsMatch(text) && (FixVerb.IsMatch(text) || !question));

        // Доработка только что созданного, ещё не сохранённого скрипта — в том числе исправление его ошибок.
        if (hasDraft && !hasScript && (broken && !question || !errorDump && !question && (Edit.IsMatch(text) || Logging.IsMatch(text) || Comments.IsMatch(text) || handling || Optimize.IsMatch(text))) && !NewWord.IsMatch(text)) return "create";
        if (errorDump && !handling) return hasScript ? "fix" : "error";
        if (wantsNew && (!hasScript || NewWord.IsMatch(text) || BasedOn.IsMatch(text))) return "create";

        if (hasScript)
        {
            if (Versions.IsMatch(text)) return "changes";
            if (broken || RetryFailure.IsMatch(text) || BareFix.IsMatch(text) || FixVerb.IsMatch(text) && (ErrorWords.IsMatch(text) || Audit.IsMatch(text) || Syntax.IsMatch(text))) return "fix";
            // Несколько видов правок сразу («добавь обработку ошибок и логирование») — общая задача изменения кода.
            if (!question && new[] { Comments.IsMatch(text), Logging.IsMatch(text), handling, Optimize.IsMatch(text) }.Count(x => x) > 1) return "complete";
            if (Comments.IsMatch(text) && !question) return "comments";
            if (Logging.IsMatch(text) && !question) return "logging";
            if (handling && !question) return "exceptions";
            if (TitleTags.IsMatch(text) && !question) return "tags";
            if (DescriptionNoun.IsMatch(text) || Detailed.IsMatch(text) && !question) return Detailed.IsMatch(text) ? "description" : "summary";
            if (Optimize.IsMatch(text) && !question) return "optimize";
            if (Explain.IsMatch(text)) return "explain";
            if (Audit.IsMatch(text)) return "audit";
            if (ErrorWords.IsMatch(text)) return "fix";
            if (Edit.IsMatch(text) && !question) return "complete";
            return "ask";
        }
        if (!question && ScriptNoun.IsMatch(text) && (CreateVerb.IsMatch(text) || Edit.IsMatch(text) || text.Contains("котор", StringComparison.OrdinalIgnoreCase))) return "create";
        if (!question && Imperative.IsMatch(text)) return "create";
        if (ErrorWords.IsMatch(text) && !handling) return "error";
        return "ask";
    }
}
