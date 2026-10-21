using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using TagLib;

namespace SoundCloudDownloader;

// Главный класс программы.
public class Program
{
    // Один клиент на всю программу. Таймаут 30 сек, маскируемся под браузер.
    public static HttpClient httpClient = CreateHttpClient();

    // Создаем HTTP-клиент. Можно передать обработчик для тестов.
    public static HttpClient CreateHttpClient(HttpMessageHandler? handler = null)
    {
        var client = handler != null ? new HttpClient(handler) : new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        return client;
    }

    // Игнорируем регистр букв в названиях полей JSON.
    internal static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    // Язык: true — русский, false — английский.
    internal static bool IsRu = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ru", StringComparison.OrdinalIgnoreCase);

    // Кэш для client_id (ключа от API).
    public static string? _cachedClientId;

    // Запасной ключ на случай, если сайт не отдал свежий.
    internal const string FallbackClientId = "bQmaW4vAHmJIUxisgjRG4MAWAiDgsk9D";

    // Имена, которые Windows не разрешает давать файлам.
    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    // Выбор строки под текущий язык (RU/EN).
    internal static string L(string ru, string en) => IsRu ? ru : en;

    static async Task<int> Main(string[] args)
    {
        // Включаем UTF-8, чтобы русский текст и спецсимволы не ломались.
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.InputEncoding = System.Text.Encoding.UTF8;

        // Если попросили справку (--help / -h / /?)
        if (args.Length > 0 && args[0] is "--help" or "-h" or "/?" or "help")
        {
            PrintHelp();
            return 0;
        }

        // Если ссылку передали аргументом: dotnet run -- "<ссылка>"
        if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
        {
            bool success = await DownloadFromUrlAsync(args[0]);
            return success ? 0 : 1;
        }

        // Иначе запускаем обычное интерактивное меню.
        SelectLanguage();
        return await RunInteractiveLoopAsync();
    }

    // Показывает справку.
    private static void PrintHelp()
    {
        Console.WriteLine(L(
            "MAGNITOLA — консольный загрузчик музыки с SoundCloud\n\n" +
            "Использование:\n" +
            "  dotnet run                      Интерактивный режим\n" +
            "  dotnet run -- \"<ссылка>\"         Прямое скачивание по ссылке\n" +
            "  dotnet run -- --help            Показать эту справку\n\n" +
            "Поддерживаемые ссылки:\n" +
            "  • Трек:      https://soundcloud.com/artist/track-name\n" +
            "  • Плейлист:  https://soundcloud.com/artist/sets/playlist-name\n" +
            "  • Лайки:     https://soundcloud.com/username (или /likes)\n" +
            "  • Короткие:  https://on.soundcloud.com/xxxx\n\n" +
            "Файлы сохраняются в папку «Загрузки» (Downloads).",

            "MAGNITOLA — SoundCloud music downloader CLI\n\n" +
            "Usage:\n" +
            "  dotnet run                      Interactive mode\n" +
            "  dotnet run -- \"<url>\"            Direct download by URL\n" +
            "  dotnet run -- --help            Show this help message\n\n" +
            "Supported links:\n" +
            "  • Track:     https://soundcloud.com/artist/track-name\n" +
            "  • Playlist:  https://soundcloud.com/artist/sets/playlist-name\n" +
            "  • Likes:     https://soundcloud.com/username (or /likes)\n" +
            "  • Shortlink: https://on.soundcloud.com/xxxx\n\n" +
            "Files are saved to your system Downloads folder."
        ));
    }

    // Меню выбора языка.
    private static void SelectLanguage()
    {
        Console.WriteLine("Select language / Выберите язык:");
        Console.WriteLine("  1. Русский (RU)" + (IsRu ? " [по умолчанию]" : ""));
        Console.WriteLine("  2. English (EN)" + (!IsRu ? " [default]" : ""));
        Console.Write("Choice / Выбор [1/2]: ");
        string? langChoice = Console.ReadLine()?.Trim();
        if (langChoice is "2" or "en" or "EN" or "english" or "English")
        {
            IsRu = false;
        }
        else if (langChoice is "1" or "ru" or "RU" or "russian" or "Russian")
        {
            IsRu = true;
        }
        Console.WriteLine();
    }

    // Рисует шапку программы.
    private static void PrintBanner(string downloadFolder)
    {
        Console.WriteLine(new string('=', 70));
        Console.WriteLine("                              MAGNITOLA                               ");
        Console.WriteLine(new string('=', 70));
        Console.WriteLine();
        Console.WriteLine(L("Краткое руководство:", "Quick Guide:"));
        Console.WriteLine(L("  • Скачивание одного трека:  вставьте ссылку на трек", "  • Single track:    paste track link"));
        Console.WriteLine(L("  • Скачивание плейлиста:     вставьте ссылку на плейлист или альбом", "  • Playlist/Album:  paste playlist or album link"));
        Console.WriteLine(L("  • Скачивание всех лайков:   вставьте ссылку на профиль пользователя", "  • All user likes:  paste user profile link"));
        Console.WriteLine();
        Console.WriteLine(L("Музыка сохраняется в папку «Загрузки»:", "Music is downloaded to the \"Downloads\" folder:"));
        Console.WriteLine($"  {downloadFolder}");
        Console.WriteLine(new string('=', 70));
        Console.WriteLine();
    }

    // Главный цикл: спрашивает ссылку, качает, потом спрашивает еще.
    private static async Task<int> RunInteractiveLoopAsync()
    {
        string downloadFolder = GetDownloadsDirectory();

        while (true)
        {
            PrintBanner(downloadFolder);

            Console.Write(L("Введите ссылку на трек, плейлист или пользователя SoundCloud: ",
                            "Enter SoundCloud track, playlist, or user profile link: "));
            string? rawUrl = Console.ReadLine();
            if (rawUrl == null) break; // Если ввод закрылся (EOF) — выходим
            string trackUrl = rawUrl.Trim();

            if (string.IsNullOrWhiteSpace(trackUrl))
            {
                Console.WriteLine(L("Ошибка: ссылка не была указана.", "Error: No link was provided."));
            }
            else
            {
                await DownloadFromUrlAsync(trackUrl);
            }

            Console.WriteLine();
            Console.WriteLine(L("Что вы хотите сделать дальше?", "What would you like to do next?"));
            Console.WriteLine(L("  1. Скачать ещё (продолжить)", "  1. Download more (continue)"));
            Console.WriteLine(L("  2. Выйти из программы", "  2. Exit program"));
            Console.Write(L("Выберите действие [1/2] (по умолчанию: 1): ", "Choose an option [1/2] (Default: 1): "));

            string? rawChoice = Console.ReadLine();
            if (rawChoice == null) break; // Если ввод закрылся — выходим
            string? choice = rawChoice.Trim();

            if (choice is "2" or "q" or "quit" or "exit" or "n" or "no" or "нет" or "н")
            {
                Console.WriteLine();
                Console.WriteLine(L("Завершение работы. До свидания!", "Exiting. Goodbye!"));
                break;
            }

            Console.WriteLine();
        }

        return 0;
    }

    // Скачивает трек, плейлист или лайки по ссылке.
    private static async Task<bool> DownloadFromUrlAsync(string rawUrl)
    {
        string downloadFolder = GetDownloadsDirectory();
        try
        {
            // 1. Получаем ключ client_id.
            Console.WriteLine(L("1. Получение Client ID...", "1. Fetching Client ID..."));
            await GetClientIdAsync();

            // 2. Чистим ссылку от лишнего и разворачиваем короткие ссылки.
            string trackUrl = await NormalizeSoundCloudUrlAsync(rawUrl);

            // 3. Узнаем через API, что за ссылка.
            Console.WriteLine(L("2. Получение информации по ссылке...", "2. Resolving URL..."));
            var (success, failedTracks) = await ResolveAndDownloadAsync(trackUrl);

            // Если что-то упало — предлагаем попробовать еще раз.
            if (failedTracks.Count > 0)
            {
                await RetryFailedTracksAsync(failedTracks);
            }

            Console.WriteLine();
            Console.WriteLine(new string('=', 70));
            Console.WriteLine(L($"Загрузка завершена! Файлы сохранены в: {downloadFolder}",
                                $"Download finished! Files saved to: {downloadFolder}"));
            Console.WriteLine(new string('=', 70));
            return success && failedTracks.Count == 0;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Console.WriteLine(L("\n[Ошибка]: Объект не найден по указанной ссылке SoundCloud.",
                                "\n[Error]: Object not found at the provided SoundCloud URL."));
            return false;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            Console.WriteLine(L("\n[Ошибка]: Ошибка авторизации API (Client ID устарел или был заблокирован).",
                                "\n[Error]: API authorization failed (Client ID expired or was blocked)."));
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine(L($"\n[Ошибка]: {ex.Message}", $"\n[Error]: {ex.Message}"));
            return false;
        }
    }

    // Узнает через API SoundCloud, что по ссылке, и качает нужное.
    public static async Task<(bool Success, List<SoundCloudTrack> FailedTracks)> ResolveAndDownloadAsync(string trackUrl)
    {
        string resolveUrl = $"https://api-v2.soundcloud.com/resolve?url={Uri.EscapeDataString(trackUrl)}";
        string json = await ApiGetStringAsync(resolveUrl);
        using var doc = JsonDocument.Parse(json);

        string kind = doc.RootElement.GetProperty("kind").GetString() ?? string.Empty;
        var failedTracks = new List<SoundCloudTrack>();

        // 1. Профиль автора -> качаем все его лайки.
        if (kind == "user")
        {
            long userId = doc.RootElement.GetProperty("id").GetInt64();
            string username = doc.RootElement.TryGetProperty("username", out var u) && u.GetString() is { Length: > 0 } name
                ? name
                : L("Неизвестный пользователь", "Unknown user");

            int totalLikes = 0;
            if (doc.RootElement.TryGetProperty("public_favorites_count", out var fav) && fav.ValueKind == JsonValueKind.Number)
            {
                totalLikes = fav.GetInt32();
            }
            else if (doc.RootElement.TryGetProperty("likes_count", out var likes) && likes.ValueKind == JsonValueKind.Number)
            {
                totalLikes = likes.GetInt32();
            }

            string countInfo = totalLikes > 0 ? L($" (всего ~{totalLikes})", $" (total ~{totalLikes})") : string.Empty;
            Console.WriteLine(L($"   Пользователь: {username}{countInfo}. Загрузка понравившихся треков (лайков)...",
                                $"   User: {username}{countInfo}. Downloading liked tracks..."));
            var (completed, likesFailures) = await DownloadLikesAsync(userId, totalLikes);
            return (completed, likesFailures);
        }

        // 2. Плейлист или альбом -> качаем все песни из него.
        if (kind == "playlist")
        {
            string title = doc.RootElement.TryGetProperty("title", out var t) && t.GetString() is { Length: > 0 } playlistTitle
                ? playlistTitle
                : L("Плейлист", "Playlist");

            Console.WriteLine(L($"   Плейлист: {title}. Загрузка треков плейлиста...",
                                $"   Playlist: {title}. Downloading playlist tracks..."));
            failedTracks = await DownloadPlaylistAsync(doc.RootElement);
            return (true, failedTracks);
        }

        // 3. Одиночный трек -> просто качаем его.
        if (kind == "track")
        {
            var track = doc.RootElement.Deserialize<SoundCloudTrack>(JsonOptions);
            if (track != null)
            {
                bool success = await ProcessTrackAsync(track, 1, 1);
                if (!success) failedTracks.Add(track);
                return (success, failedTracks);
            }
            return (false, failedTracks);
        }

        Console.WriteLine(L($"Неподдерживаемый тип ссылки: {kind}. Поддерживаются треки, плейлисты и профили пользователей.",
                            $"Unsupported link type: {kind}. Supported: tracks, playlists, and user profiles."));
        return (false, failedTracks);
    }

    // GET-запрос к API. Сам подставляет client_id в URL.
    public static async Task<string> ApiGetStringAsync(string url)
    {
        return await ApiGetStringAsync(clientId =>
        {
            if (Regex.IsMatch(url, @"([?&])client_id=[^&]*"))
            {
                return Regex.Replace(url, @"([?&])client_id=[^&]*", $"$1client_id={clientId}");
            }
            return url.Contains('?') ? $"{url}&client_id={clientId}" : $"{url}?client_id={clientId}";
        });
    }

    // GET-запрос к API. Если словили 401 (ключ протух) — берет свежий ключ и пробует еще 1 раз.
    public static async Task<string> ApiGetStringAsync(Func<string, string> urlFactory)
    {
        string clientId = await GetClientIdAsync();
        string url = urlFactory(clientId);
        try
        {
            return await httpClient.GetStringAsync(url);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            // Сбрасываем старый ключ и ищем свежий
            _cachedClientId = null;
            string newClientId;
            try
            {
                newClientId = await GetClientIdAsync(forceRefresh: true);
            }
            catch (Exception refreshEx)
            {
                throw new HttpRequestException(
                    L($"Не удалось обновить Client ID: {refreshEx.Message}", $"Failed to refresh Client ID: {refreshEx.Message}"),
                    ex,
                    System.Net.HttpStatusCode.Unauthorized
                );
            }

            // Повторяем запрос со свежим ключом ровно 1 раз
            string retryUrl = urlFactory(newClientId);
            return await httpClient.GetStringAsync(retryUrl);
        }
    }

    // Приводит ссылку в порядок: разворачивает короткие ссылки и убирает хвосты.
    internal static async Task<string> NormalizeSoundCloudUrlAsync(string url)
    {
        url = url.Trim();
        if (string.IsNullOrWhiteSpace(url)) return url;

        // Если ссылка короткая (on.soundcloud.com) — узнаем полный адрес
        if (url.Contains("on.soundcloud.com", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                using var res = await httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                if (res.RequestMessage?.RequestUri != null)
                {
                    url = res.RequestMessage.RequestUri.ToString();
                }
            }
            catch { }
        }

        // Мобильный soundcloud меняем на обычный
        if (url.StartsWith("https://m.soundcloud.com", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://soundcloud.com" + url["https://m.soundcloud.com".Length..];
        }
        else if (url.StartsWith("http://m.soundcloud.com", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://soundcloud.com" + url["http://m.soundcloud.com".Length..];
        }

        // Отрезаем ?si=... и прочий хвост
        string pathAndDomain = url;
        string query = string.Empty;
        int queryIdx = url.IndexOf('?');
        if (queryIdx >= 0)
        {
            pathAndDomain = url[..queryIdx];
            query = url[queryIdx..];
        }

        pathAndDomain = pathAndDomain.TrimEnd('/');
        if (pathAndDomain.EndsWith("/likes", StringComparison.OrdinalIgnoreCase))
        {
            pathAndDomain = pathAndDomain[..^6].TrimEnd('/');
            return pathAndDomain;
        }

        return pathAndDomain + query;
    }

    // Спрашивает пользователя, надо ли перекачать упавшие треки.
    private static async Task RetryFailedTracksAsync(List<SoundCloudTrack> failedTracks)
    {
        while (failedTracks.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine(L($"Не удалось скачать треков: {failedTracks.Count}", $"Failed to download {failedTracks.Count} track(s)."));
            Console.Write(L("Попробовать скачать неудавшиеся треки снова? [Y/n] (по умолчанию: Да): ", "Retry downloading failed tracks? [Y/n] (default: Yes): "));
            string? raw = Console.ReadLine();
            if (raw == null) break; // Если ввод закрылся — выходим
            string input = raw.Trim();
            if (input is "n" or "no" or "н" or "нет") break;

            Console.WriteLine();
            Console.WriteLine(L("Повторная попытка загрузки треков...", "Retrying failed tracks..."));
            var currentBatch = new List<SoundCloudTrack>(failedTracks);
            failedTracks.Clear();

            // Если у треков не было данных из-за ошибки в плейлисте — дозапрашиваем их по ID
            var missingMeta = currentBatch.Where(t => t.Media == null && t.Id > 0).ToList();
            if (missingMeta.Count > 0)
            {
                for (int b = 0; b < missingMeta.Count; b += 50)
                {
                    var batchIds = missingMeta.Skip(b).Take(50).Select(t => t.Id).ToList();
                    string idsParam = string.Join("%2C", batchIds);
                    try
                    {
                        string batchJson = await ApiGetStringAsync($"https://api-v2.soundcloud.com/tracks?ids={idsParam}");
                        using var batchDoc = JsonDocument.Parse(batchJson);
                        var restored = new Dictionary<long, SoundCloudTrack>();
                        foreach (var elem in batchDoc.RootElement.EnumerateArray())
                        {
                            var track = elem.Deserialize<SoundCloudTrack>(JsonOptions);
                            if (track != null && track.Id > 0) restored[track.Id] = track;
                        }
                        for (int i = 0; i < currentBatch.Count; i++)
                        {
                            if (currentBatch[i].Id > 0 && restored.TryGetValue(currentBatch[i].Id, out var fullTrack))
                            {
                                currentBatch[i] = fullTrack;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(L($"   ! Ошибка при повторном получении информации о треках: {ex.Message}",
                                            $"   ! Error refetching tracks information: {ex.Message}"));
                    }
                }
            }

            for (int i = 0; i < currentBatch.Count; i++)
            {
                var track = currentBatch[i];
                bool success = await ProcessTrackAsync(track, i + 1, currentBatch.Count);
                if (!success) failedTracks.Add(track);
            }
        }
    }

    // Скачивает плейлист. Сохраняет порядок треков как в альбоме.
    public static async Task<List<SoundCloudTrack>> DownloadPlaylistAsync(JsonElement playlistElement)
    {
        var failedTracks = new List<SoundCloudTrack>();
        if (!playlistElement.TryGetProperty("tracks", out var tracksElement) || tracksElement.ValueKind != JsonValueKind.Array)
        {
            Console.WriteLine(L("   Плейлист пуст или не содержит треков.", "   Playlist is empty or contains no tracks."));
            return failedTracks;
        }

        var orderedIds = new List<long>();
        var tracksById = new Dictionary<long, SoundCloudTrack>();
        var tracksToFetch = new List<long>();

        // Запоминаем порядок треков и сохраняем те, по которым данные уже пришли
        foreach (var trackItem in tracksElement.EnumerateArray())
        {
            if (trackItem.TryGetProperty("id", out var idElem))
            {
                long id = idElem.GetInt64();
                orderedIds.Add(id);

                if (trackItem.TryGetProperty("media", out _))
                {
                    var track = trackItem.Deserialize<SoundCloudTrack>(JsonOptions);
                    if (track != null) tracksById[id] = track;
                }
                else
                {
                    tracksToFetch.Add(id);
                }
            }
        }

        // По трекам, где есть только ID, запрашиваем данные пачками по 50 штук
        if (tracksToFetch.Count > 0)
        {
            for (int i = 0; i < tracksToFetch.Count; i += 50)
            {
                var batch = tracksToFetch.Skip(i).Take(50);
                string idsParam = string.Join("%2C", batch);
                string tracksUrl = $"https://api-v2.soundcloud.com/tracks?ids={idsParam}";
                try
                {
                    string batchJson = await ApiGetStringAsync(tracksUrl);
                    using var batchDoc = JsonDocument.Parse(batchJson);
                    foreach (var trackElem in batchDoc.RootElement.EnumerateArray())
                    {
                        if (trackElem.TryGetProperty("id", out var idElem))
                        {
                            var track = trackElem.Deserialize<SoundCloudTrack>(JsonOptions);
                            if (track != null) tracksById[idElem.GetInt64()] = track;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(L($"   ! Ошибка при получении информации о треках: {ex.Message}",
                                        $"   ! Error fetching tracks information: {ex.Message}"));
                }
            }
        }

        // Собираем треки в том порядке, в каком они идут в плейлисте
        var tracks = new List<SoundCloudTrack>();
        var missingIds = new List<long>();

        foreach (long id in orderedIds)
        {
            if (tracksById.TryGetValue(id, out var track))
            {
                tracks.Add(track);
            }
            else
            {
                missingIds.Add(id);
                // Записываем в список ошибок, если данные трека так и не пришли
                failedTracks.Add(new SoundCloudTrack(
                    Id: id,
                    Title: $"Track ID: {id}",
                    ArtworkUrl: null,
                    TrackAuthorization: null,
                    User: null,
                    Media: null
                ));
            }
        }

        if (missingIds.Count > 0)
        {
            Console.WriteLine(L(
                $"   [!] Не удалось получить метаданные для {missingIds.Count} трек(ов) из плейлиста (ID: {string.Join(", ", missingIds.Take(10))}{(missingIds.Count > 10 ? "..." : "")}).",
                $"   [!] Failed to fetch metadata for {missingIds.Count} playlist track(s) (IDs: {string.Join(", ", missingIds.Take(10))}{(missingIds.Count > 10 ? "..." : "")})."
            ));
        }

        // Качаем каждый трек по очереди
        for (int i = 0; i < tracks.Count; i++)
        {
            bool success = await ProcessTrackAsync(tracks[i], i + 1, orderedIds.Count);
            if (!success) failedTracks.Add(tracks[i]);
        }

        return failedTracks;
    }

    // Качает все лайки пользователя страницами по 100 штук.
    internal static async Task<(bool Completed, List<SoundCloudTrack> FailedTracks)> DownloadLikesAsync(long userId, int totalLikes)
    {
        string? url = $"https://api-v2.soundcloud.com/users/{userId}/likes?limit=100";
        var failedTracks = new List<SoundCloudTrack>();
        int currentTrack = 0;
        bool completed = true;

        while (!string.IsNullOrEmpty(url))
        {
            string json;
            try
            {
                json = await ApiGetStringAsync(url);
            }
            catch (Exception ex)
            {
                Console.WriteLine(L($"   ! Ошибка загрузки списка лайков: {ex.Message}",
                                    $"   ! Error loading likes list: {ex.Message}"));
                completed = false;
                break;
            }

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("collection", out var collection) || collection.ValueKind != JsonValueKind.Array)
            {
                break;
            }

            foreach (var item in collection.EnumerateArray())
            {
                if (item.TryGetProperty("track", out var trackElem))
                {
                    var track = trackElem.Deserialize<SoundCloudTrack>(JsonOptions);
                    if (track?.Media?.Transcodings != null)
                    {
                        currentTrack++;
                        bool success = await ProcessTrackAsync(track, currentTrack, totalLikes);
                        if (!success) failedTracks.Add(track);
                    }
                }
            }

            // Ссылка на следующую страницу (next_href)
            if (doc.RootElement.TryGetProperty("next_href", out var nextHrefElem) && nextHrefElem.ValueKind == JsonValueKind.String)
            {
                url = nextHrefElem.GetString();
            }
            else
            {
                url = null;
            }
        }

        return (completed, failedTracks);
    }

    // Качает один трек: выбирает поток, пишет во временный файл, вшивает теги и обложку.
    internal static async Task<bool> ProcessTrackAsync(SoundCloudTrack track, int currentTrackIndex, int totalTracks)
    {
        string title = string.IsNullOrWhiteSpace(track.Title) ? L("Без названия", "Untitled") : track.Title.Trim();
        string artist = string.IsNullOrWhiteSpace(track.User?.Username) ? L("Неизвестный исполнитель", "Unknown artist") : track.User.Username.Trim();

        string outputDirectory = GetDownloadsDirectory();
        Directory.CreateDirectory(outputDirectory);

        string baseFileName = SanitizeFileName($"{artist} - {title}");
        string pathM4a = Path.Combine(outputDirectory, $"{baseFileName}.m4a");
        string pathMp3 = Path.Combine(outputDirectory, $"{baseFileName}.mp3");

        // Если файл уже скачан (.m4a или .mp3) — пропускаем
        if (System.IO.File.Exists(pathM4a) || System.IO.File.Exists(pathMp3))
        {
            string counterStr = totalTracks > 0 ? $"[{currentTrackIndex}/{totalTracks}]" : $"[{currentTrackIndex}]";
            string existing = System.IO.File.Exists(pathM4a) ? $"{baseFileName}.m4a" : $"{baseFileName}.mp3";
            Console.WriteLine(L($"   ✓ {counterStr} Уже существует: {existing}",
                                $"   ✓ {counterStr} Already exists: {existing}"));
            return true;
        }

        var transcodings = track.Media?.Transcodings;
        if (transcodings == null || transcodings.Length == 0)
        {
            Console.WriteLine(L(
                $"   [!] {artist} - {title}: аудиопотоки не найдены.",
                $"   [!] {artist} - {title}: audio streams not found."
            ));
            return false;
        }

        // Проверяем, не подписочный ли это трек (SoundCloud Go+ с DRM)
        bool onlyEncrypted = transcodings.All(t =>
            t.Format?.Protocol != null &&
            (t.Format.Protocol.Contains("encrypted") || t.Format.Protocol.Contains("drm")));

        if (onlyEncrypted)
        {
            Console.WriteLine(L(
                $"   [!] {artist} - {title}: трек доступен только по платной подписке SoundCloud Go+ (защищён DRM).",
                $"   [!] {artist} - {title}: track requires SoundCloud Go+ subscription (DRM protected)."
            ));
            return false;
        }

        // Сначала ищем лучший поток: HLS AAC 160k (или 96k)
        var hlsTranscoding = transcodings
            .Where(t => t.Format?.Protocol == "hls" &&
                        t.Format?.MimeType?.Contains("mp4") == true &&
                        !t.Snipped)
            .OrderByDescending(t => t.Url.Contains("aac_160k") ? 2 : 1)
            .FirstOrDefault();

        // Если HLS нет — берем обычный прямой MP3
        var progTranscoding = transcodings
            .FirstOrDefault(t => t.Format?.Protocol == "progressive" && !t.Snipped);

        if (hlsTranscoding == null && progTranscoding == null)
        {
            if (transcodings.Any(t => t.Snipped))
            {
                Console.WriteLine(L(
                    $"   [!] {artist} - {title}: доступен только 30-секундный превью-фрагмент (полный трек защищен).",
                    $"   [!] {artist} - {title}: only 30-second preview snippet available (full track is restricted)."
                ));
            }
            else
            {
                Console.WriteLine(L(
                    $"   [!] {artist} - {title}: подходящий аудиопоток не найден.",
                    $"   [!] {artist} - {title}: suitable audio stream not found."
                ));
            }
            return false;
        }

        bool isHls = hlsTranscoding != null;
        var selectedTranscoding = hlsTranscoding ?? progTranscoding!;
        string extension = isHls ? ".m4a" : ".mp3";
        string mimeType = isHls ? "audio/mp4" : "audio/mpeg";
        string outputPath = Path.Combine(outputDirectory, $"{baseFileName}{extension}");
        string tempPath = Path.Combine(outputDirectory, $".tmp_{baseFileName}{extension}");

        try
        {
            // Получаем прямую ссылку на аудиопоток
            string streamEndpointUrl = await GetAudioStreamUrlAsync(selectedTranscoding.Url, track.TrackAuthorization);

            if (isHls)
            {
                await DownloadHlsWithProgressAsync(streamEndpointUrl, tempPath, artist, title, currentTrackIndex, totalTracks);
            }
            else
            {
                await DownloadFileWithProgressAsync(streamEndpointUrl, tempPath, artist, title, currentTrackIndex, totalTracks);
            }

            // Записываем теги и обложку
            await EmbedMetadataAsync(tempPath, mimeType, title, artist, track.ArtworkUrl ?? track.User?.AvatarUrl);

            // Убираем приставку .tmp_ из имени файла
            System.IO.File.Move(tempPath, outputPath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            // Если что-то пошло не так — удаляем недокачанный временный файл
            if (System.IO.File.Exists(tempPath))
            {
                try { System.IO.File.Delete(tempPath); } catch { }
            }

            Console.WriteLine(L($"   ! Ошибка при скачивании трека: {ex.Message}",
                                $"   ! Error downloading track: {ex.Message}"));
            return false;
        }
    }

    // Качает песню по кусочкам (HLS m3u8) и собирает в один .m4a файл с прогресс-баром.
    private static async Task DownloadHlsWithProgressAsync(
        string playlistUrl,
        string destinationPath,
        string artist,
        string title,
        int currentTrackIndex,
        int totalTracks)
    {
        string m3u8Text = await httpClient.GetStringAsync(playlistUrl);
        var baseUri = new Uri(playlistUrl);
        string? initUrl = null;
        var segUrls = new List<string>();

        var mapMatch = Regex.Match(m3u8Text, @"#EXT-X-MAP:URI=[""']([^""']+)[""']");
        if (mapMatch.Success)
        {
            initUrl = new Uri(baseUri, mapMatch.Groups[1].Value).ToString();
        }

        foreach (var rawLine in m3u8Text.Split('\n'))
        {
            string line = rawLine.Trim();
            if (!string.IsNullOrEmpty(line) && !line.StartsWith("#"))
            {
                segUrls.Add(new Uri(baseUri, line).ToString());
            }
        }

        if (segUrls.Count == 0)
        {
            throw new Exception(L("HLS плейлист не содержит аудиосегментов.", "HLS playlist contains no audio segments."));
        }

        string countStr = totalTracks > 0 ? $"{currentTrackIndex}/{totalTracks}" : $"{currentTrackIndex}";
        Console.WriteLine(L($"🎵 [{countStr}] Скачивание (HLS): {artist} - {title}",
                            $"🎵 [{countStr}] Downloading (HLS): {artist} - {title}"));

        long totalBytesRead = 0;
        using (var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true))
        {
            // Заголовок файла (init.mp4)
            if (initUrl != null)
            {
                byte[] initBytes = await DownloadBytesWithRetryAsync(initUrl);
                await fileStream.WriteAsync(initBytes);
                totalBytesRead += initBytes.Length;
            }

            // Качаем сами кусочки музыки (.m4s)
            for (int i = 0; i < segUrls.Count; i++)
            {
                byte[] segBytes = await DownloadBytesWithRetryAsync(segUrls[i]);
                await fileStream.WriteAsync(segBytes);
                totalBytesRead += segBytes.Length;

                int downloadedSegs = i + 1;
                double percentage = (double)downloadedSegs / segUrls.Count * 100.0;
                string bar = GetProgressBar(percentage);
                double mbRead = totalBytesRead / 1048576.0;

                Console.Write($"\r   [{bar}] {percentage,5:F0}% ({downloadedSegs}/{segUrls.Count})  {mbRead:F2} MB");
            }
        }

        Console.WriteLine(L("  ✓ Завершено", "  ✓ Completed"));
    }

    // Скачивает кусок файла. Если сеть моргнула — пробует еще пару раз.
    private static async Task<byte[]> DownloadBytesWithRetryAsync(string url, int maxRetries = 3)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await httpClient.GetByteArrayAsync(url);
            }
            catch when (attempt < maxRetries)
            {
                await Task.Delay(500 * attempt);
            }
        }
    }

    // Вшивает в файл автора, название и обложку 500x500.
    private static async Task EmbedMetadataAsync(string filePath, string mimeType, string title, string artist, string? artworkUrl)
    {
        try
        {
            using var file = TagLib.File.Create(filePath, mimeType, TagLib.ReadStyle.Average);
            file.Tag.Title = title;
            file.Tag.Performers = new[] { artist };

            if (!string.IsNullOrEmpty(artworkUrl))
            {
                try
                {
                    string highResArtwork = artworkUrl.Replace("-large", "-t500x500");
                    byte[] artworkBytes = await httpClient.GetByteArrayAsync(highResArtwork);

                    // Проверяем формат картинки (PNG или JPEG)
                    string artMime = (artworkBytes.Length > 3 && artworkBytes[0] == 0x89 && artworkBytes[1] == 0x50 && artworkBytes[2] == 0x4E)
                        ? "image/png"
                        : "image/jpeg";

                    file.Tag.Pictures = new TagLib.IPicture[]
                    {
                        new TagLib.Picture(new TagLib.ByteVector(artworkBytes))
                        {
                            Type = TagLib.PictureType.FrontCover,
                            MimeType = artMime,
                            Description = "Cover"
                        }
                    };
                }
                catch
                {
                    // Если обложка не скачалась — ничего страшного, трек все равно сохраняем
                }
            }

            file.Save();
        }
        catch
        {
            // Если теги не записались — файл не трогаем
        }
    }

    // Ищет свежий client_id в скриптах на сайте. Если не нашел — берет запасной.
    internal static async Task<string> GetClientIdAsync(bool forceRefresh = false)
    {
        if (!forceRefresh && !string.IsNullOrEmpty(_cachedClientId))
        {
            return _cachedClientId;
        }

        try
        {
            string mainPageHtml = await httpClient.GetStringAsync("https://soundcloud.com");
            var scriptMatches = Regex.Matches(mainPageHtml, @"src=[""'](https://[a-zA-Z0-9\-]+\.sndcdn\.com/assets/[^""']+\.js)[""']");

            // Смотрим скрипты с конца: нужный код обычно лежит в последних файлах
            foreach (Match match in scriptMatches.Cast<Match>().Reverse())
            {
                try
                {
                    string scriptContent = await httpClient.GetStringAsync(match.Groups[1].Value);
                    var clientMatch = Regex.Match(scriptContent, @"client_id[:=]\s*[""']([a-zA-Z0-9]{32,64})[""']");
                    if (clientMatch.Success)
                    {
                        return _cachedClientId = clientMatch.Groups[1].Value;
                    }
                }
                catch
                {
                    // Если один скрипт не скачался — идем к следующему
                }
            }
        }
        catch { }

        // Если ключ на сайте найти не удалось — берем запасной
        return _cachedClientId = FallbackClientId;
    }

    // Получает прямую ссылку на аудиопоток у API.
    public static async Task<string> GetAudioStreamUrlAsync(string transcodingUrl, string? trackAuth)
    {
        string json = await ApiGetStringAsync(clientId =>
        {
            string url = $"{transcodingUrl}?client_id={clientId}";
            if (!string.IsNullOrEmpty(trackAuth))
            {
                url += $"&track_authorization={Uri.EscapeDataString(trackAuth)}";
            }
            return url;
        });

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("url", out var urlElement))
        {
            return urlElement.GetString() ?? throw new Exception(L("Получен пустой URL аудиопотока.", "Received an empty audio stream URL."));
        }

        throw new Exception(L("Не удалось извлечь URL потока из ответа API.", "Failed to extract stream URL from API response."));
    }

    // Качает обычный прямой файл (MP3) с полосой загрузки.
    private static async Task DownloadFileWithProgressAsync(
        string downloadUrl,
        string destinationPath,
        string artist,
        string title,
        int currentTrackIndex,
        int totalTracks)
    {
        using var response = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        long totalBytes = response.Content.Headers.ContentLength ?? -1L;
        using var contentStream = await response.Content.ReadAsStreamAsync();
        using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 16384, true);

        byte[] buffer = new byte[16384];
        long totalBytesRead = 0;
        int bytesRead;

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        long lastRenderTicks = 0;
        string countStr = totalTracks > 0 ? $"{currentTrackIndex}/{totalTracks}" : $"{currentTrackIndex}";

        Console.WriteLine(L($"🎵 [{countStr}] Скачивание: {artist} - {title}",
                            $"🎵 [{countStr}] Downloading: {artist} - {title}"));

        while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
            totalBytesRead += bytesRead;

            long elapsed = stopwatch.ElapsedMilliseconds;
            if (elapsed - lastRenderTicks > 80 || totalBytesRead == totalBytes)
            {
                lastRenderTicks = elapsed;
                double mbRead = totalBytesRead / 1048576.0;

                if (totalBytes > 0)
                {
                    double percentage = (double)totalBytesRead / totalBytes * 100.0;
                    double mbTotal = totalBytes / 1048576.0;
                    string bar = GetProgressBar(percentage);
                    Console.Write($"\r   [{bar}] {percentage,5:F1}%  {mbRead:F2} / {mbTotal:F2} MB");
                }
                else
                {
                    Console.Write($"\r   {mbRead:F2} MB");
                }
            }
        }

        Console.WriteLine(L("  ✓ Завершено", "  ✓ Completed"));
    }

    // Рисует полоску прогресса: █ и ░.
    internal static string GetProgressBar(double percentage, int width = 30)
    {
        int filled = Math.Clamp((int)(percentage / 100.0 * width), 0, width);
        return new string('█', filled) + new string('░', width - filled);
    }

    // Убирает запрещенные символы из имени файла и режет длину до 180 символов.
    public static string SanitizeFileName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();

        string clean = string.Create(name.Length, (name, invalidChars), (span, state) =>
        {
            for (int i = 0; i < state.name.Length; i++)
            {
                char c = state.name[i];
                span[i] = state.invalidChars.Contains(c) ? '_' : c;
            }
        }).Trim(' ', '.');

        const int maxLen = 180;
        if (clean.Length > maxLen)
        {
            clean = clean[..maxLen].TrimEnd(' ', '.');
        }

        if (string.IsNullOrWhiteSpace(clean))
        {
            return $"track_{DateTime.UtcNow.Ticks}";
        }

        if (WindowsReservedNames.Contains(clean))
        {
            return $"_{clean}";
        }

        return clean;
    }

    // Путь к папке «Загрузки» (Downloads) текущего пользователя.
    internal static string GetDownloadsDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
}

// Модели для чтения ответов от SoundCloud API.

// Данные трека.
public record SoundCloudTrack(
    long Id = 0,
    string? Title = null,
    [property: JsonPropertyName("artwork_url")] string? ArtworkUrl = null,
    [property: JsonPropertyName("track_authorization")] string? TrackAuthorization = null,
    SoundCloudUser? User = null,
    SoundCloudMedia? Media = null
);

// Автор трека.
public record SoundCloudUser(
    string? Username,
    [property: JsonPropertyName("avatar_url")] string? AvatarUrl
);

// Список аудиопотоков трека.
public record SoundCloudMedia(Transcoding[]? Transcodings);

// Один аудиопоток трека.
public record Transcoding(
    string Url,
    TranscodingFormat? Format,
    [property: JsonPropertyName("snipped")] bool Snipped
);

// Формат потока (hls или progressive).
public record TranscodingFormat(
    string Protocol,
    [property: JsonPropertyName("mime_type")] string? MimeType
);
