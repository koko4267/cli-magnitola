using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TagLib;

namespace SoundCloudDownloader;

/// <summary>
/// Главный класс консольного загрузчика треков, плейлистов и лайков с SoundCloud.
/// </summary>
class Program
{
    // Единый экземпляр HttpClient для всех сетевых запросов с таймаутом в 30 секунд
    private static readonly HttpClient httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    // Набор локализованных строк для русского языка
    private static readonly LocalizedStrings RuStrings = new()
    {
        HeaderTitle = "                              MAGNITOLA                               ",
        QuickGuideHeader = "Краткое руководство:",
        QuickGuideTrack = "  • Скачивание одного трека:  вставьте ссылку на трек",
        QuickGuidePlaylist = "  • Скачивание плейлиста:     вставьте ссылку на плейлист или альбом",
        QuickGuideUser = "  • Скачивание всех лайков:   вставьте ссылку на профиль пользователя",
        SaveLocationInfo = "Музыка сохраняется в папку «Загрузки»:",
        UrlPrompt = "Введите ссылку на трек, плейлист или пользователя SoundCloud: ",
        ErrorEmptyUrl = "Ошибка: ссылка не была указана.",
        Step1ClientId = "1. Получение Client ID...",
        Step2Resolve = "2. Получение информации по ссылке...",
        UserFound = "   Пользователь: {0}{1}. Загрузка понравившихся треков (лайков)...",
        TotalApprox = " (всего ~{0})",
        PlaylistFound = "   Плейлист: {0}. Загрузка треков плейлиста...",
        PlaylistDefaultTitle = "Плейлист",
        UnknownUser = "Неизвестный пользователь",
        UnknownArtist = "Неизвестный исполнитель",
        UntitledTrack = "Без названия",
        PlaylistEmpty = "   Плейлист пуст или не содержит треков.",
        TrackProcessError = "   Ошибка обработки трека: {0}",
        UnsupportedKind = "Неподдерживаемый тип ссылки: {0}. Поддерживаются треки, плейлисты и профили пользователей.",
        CompletedSummary = "Загрузка завершена! Файлы сохранены в: {0}",
        ErrorPrefix = "\n[Ошибка]: {0}",
        TracksInfoError = "   ! Ошибка при получении информации о треках: {0}",
        TrackDownloadError = "   ! Ошибка при скачивании трека: {0}",
        StreamNotFound = "   [!] {0} - {1}: подходящий аудиопоток не найден.",
        AlreadyExists = "   ✓ {0} Уже существует: {1} - {2}",
        DownloadingHeader = "🎵 [{0}] Скачивание: {1} - {2}",
        StatsSize = "   Размер: {0:F2} / {1:F2} МБ  •  Трек {2} из {3}",
        StatsSizeSingle = "   Размер: {0:F2} / {1:F2} МБ",
        StatsDownloaded = "   Загружено: {0:F2} МБ",
        CompletedStats = "   ✓ Завершено: {0:F2} МБ",
        FailedTracksCount = "Не удалось скачать треков: {0}",
        RetryPrompt = "Попробовать скачать неудавшиеся треки снова? [Y/n] (по умолчанию: Да): ",
        RetryingFailedTracks = "Повторная попытка загрузки треков...",
        NextActionHeader = "Что вы хотите сделать дальше?",
        NextActionContinue = "  1. Скачать ещё (продолжить)",
        NextActionExit = "  2. Выйти из программы",
        NextActionPrompt = "Выберите действие [1/2] (по умолчанию: 1): ",
        Goodbye = "Завершение работы. До свидания!",
        EmptyAudioUrlError = "Получен пустой URL аудиопотока.",
        StreamUrlExtractError = "Не удалось извлечь URL потока из ответа API.",
        Quotes = new[]
        {
            "Компьютеры придумал господь Бог",
            "Хочешь поиграть в хоррор?",
            "Если я поддерживаю ******* если я уважаю ******* то я что? ****** чтоли?",
            "мой мак мой мак сейчас монтирует доту",
            "я выучил фреймворк по дотнету",
            "колесо смерти"
        }
    };

    // Набор локализованных строк для английского языка
    private static readonly LocalizedStrings EnStrings = new()
    {
        HeaderTitle = "                              MAGNITOLA                               ",
        QuickGuideHeader = "Quick Guide:",
        QuickGuideTrack = "  • Single track:    paste track link",
        QuickGuidePlaylist = "  • Playlist/Album:  paste playlist or album link",
        QuickGuideUser = "  • All user likes:  paste user profile link",
        SaveLocationInfo = "Music is downloaded to the \"Downloads\" folder:",
        UrlPrompt = "Enter SoundCloud track, playlist, or user profile link: ",
        ErrorEmptyUrl = "Error: No link was provided.",
        Step1ClientId = "1. Fetching Client ID...",
        Step2Resolve = "2. Resolving URL...",
        UserFound = "   User: {0}{1}. Downloading liked tracks...",
        TotalApprox = " (total ~{0})",
        PlaylistFound = "   Playlist: {0}. Downloading playlist tracks...",
        PlaylistDefaultTitle = "Playlist",
        UnknownUser = "Unknown user",
        UnknownArtist = "Unknown artist",
        UntitledTrack = "Untitled",
        PlaylistEmpty = "   Playlist is empty or contains no tracks.",
        TrackProcessError = "   Error processing track: {0}",
        UnsupportedKind = "Unsupported link type: {0}. Supported: tracks, playlists, and user profiles.",
        CompletedSummary = "Download finished! Files saved to: {0}",
        ErrorPrefix = "\n[Error]: {0}",
        TracksInfoError = "   ! Error fetching tracks information: {0}",
        TrackDownloadError = "   ! Error downloading track: {0}",
        StreamNotFound = "   [!] {0} - {1}: suitable audio stream not found.",
        AlreadyExists = "   ✓ {0} Already exists: {1} - {2}",
        DownloadingHeader = "🎵 [{0}] Downloading: {1} - {2}",
        StatsSize = "   Size: {0:F2} / {1:F2} MB  •  Track {2} of {3}",
        StatsSizeSingle = "   Size: {0:F2} / {1:F2} MB",
        StatsDownloaded = "   Downloaded: {0:F2} MB",
        CompletedStats = "   ✓ Completed: {0:F2} MB",
        FailedTracksCount = "Failed to download {0} track(s).",
        RetryPrompt = "Retry downloading failed tracks? [Y/n] (default: Yes): ",
        RetryingFailedTracks = "Retrying failed tracks...",
        NextActionHeader = "What would you like to do next?",
        NextActionContinue = "  1. Download more (continue)",
        NextActionExit = "  2. Exit program",
        NextActionPrompt = "Choose an option [1/2] (Default: 1): ",
        Goodbye = "Exiting. Goodbye!",
        EmptyAudioUrlError = "Received an empty audio stream URL.",
        StreamUrlExtractError = "Failed to extract stream URL from API response.",
        Quotes = new[]
        {
            "God invented computers",
            "Do you want to play a horror game?",
            "If I support ******* if I respect ******* then what am I? ****** or something?",
            "my mac my mac is rendering dota right now",
            "I learned a .NET framework",
            "wheel of death"
        }
    };

    // Текущий активный язык (по умолчанию русский)
    private static LocalizedStrings L = RuStrings;

    /// <summary>
    /// Точка входа в приложение.
    /// </summary>
    static async Task Main(string[] args)
    {
        // Установка кодировки UTF-8 для корректного отображения символов и эмодзи в консоли
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // Выбор языка интерфейса при запуске
        Console.WriteLine("Select language / Выберите язык:");
        Console.WriteLine("  1. Русский (RU)");
        Console.WriteLine("  2. English (EN)");
        Console.Write("Choice / Выбор [1/2] (Default: 1): ");
        string? langInput = Console.ReadLine()?.Trim();
        if (langInput == "2" || langInput?.Equals("en", StringComparison.OrdinalIgnoreCase) == true || langInput?.Equals("english", StringComparison.OrdinalIgnoreCase) == true)
        {
            L = EnStrings;
        }
        else
        {
            L = RuStrings;
        }
        Console.WriteLine();

        // Определение кроссплатформенного пути к папке Загрузки/magnitola
        string downloadFolder = GetDownloadsDirectory();
        string initialArgUrl = args.Length > 0 ? args[0] : string.Empty;

        // Установка стандартного User-Agent браузера, чтобы SoundCloud не блокировал запросы
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36"
        );

        // Главный интерактивный цикл работы приложения
        while (true)
        {
            // Вывод заголовка и краткого руководства
            Console.WriteLine(new string('=', 70));
            Console.WriteLine(L.HeaderTitle);
            Console.WriteLine(new string('=', 70));
            Console.WriteLine();
            Console.WriteLine(L.QuickGuideHeader);
            Console.WriteLine(L.QuickGuideTrack);
            Console.WriteLine(L.QuickGuidePlaylist);
            Console.WriteLine(L.QuickGuideUser);
            Console.WriteLine();
            Console.WriteLine(L.SaveLocationInfo);
            Console.WriteLine($"  {downloadFolder}");
            Console.WriteLine(new string('=', 70));
            Console.WriteLine();

            // Получение ссылки из аргументов командной строки (только в первой итерации) или через ввод пользователя
            string trackUrl = string.Empty;
            if (!string.IsNullOrWhiteSpace(initialArgUrl))
            {
                trackUrl = initialArgUrl;
                initialArgUrl = string.Empty;
                Console.WriteLine($"{L.UrlPrompt}{trackUrl}");
            }
            else
            {
                Console.Write(L.UrlPrompt);
                trackUrl = Console.ReadLine()?.Trim() ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(trackUrl))
            {
                Console.WriteLine(L.ErrorEmptyUrl);
            }
            else
            {
                try
                {
                    // Шаг 1: Получение актуального client_id из скриптов сайта
                    Console.WriteLine(L.Step1ClientId);
                    string clientId = await GetClientIdAsync();
                    Console.WriteLine($"   Client ID: {clientId}");

                    // Если ссылка заканчивается на /likes, убираем этот суффикс для корректного резолва пользователя
                    if (trackUrl.EndsWith("/likes", StringComparison.OrdinalIgnoreCase))
                    {
                        trackUrl = trackUrl.Substring(0, trackUrl.Length - 6);
                    }

                    // Шаг 2: Получение метаданных по ссылке через API v2 resolve
                    Console.WriteLine(L.Step2Resolve);
                    string resolveUrl = $"https://api-v2.soundcloud.com/resolve?url={Uri.EscapeDataString(trackUrl)}&client_id={clientId}";
                    string json = await httpClient.GetStringAsync(resolveUrl);
                    using var doc = JsonDocument.Parse(json);

                    // Определение типа сущности SoundCloud (пользователь, плейлист или трек)
                    var kind = doc.RootElement.GetProperty("kind").GetString();
                    var failedTracks = new List<SoundCloudTrack>();

                    if (kind == "user")
                    {
                        // Загрузка всех понравившихся треков пользователя
                        long userId = doc.RootElement.GetProperty("id").GetInt64();
                        string username = doc.RootElement.GetProperty("username").GetString() ?? L.UnknownUser;
                        int totalLikes = 0;
                        if (doc.RootElement.TryGetProperty("public_favorites_count", out var favElem) && favElem.ValueKind == JsonValueKind.Number)
                        {
                            totalLikes = favElem.GetInt32();
                        }
                        else if (doc.RootElement.TryGetProperty("likes_count", out var likesElem) && likesElem.ValueKind == JsonValueKind.Number)
                        {
                            totalLikes = likesElem.GetInt32();
                        }

                        string countInfo = totalLikes > 0 ? string.Format(L.TotalApprox, totalLikes) : string.Empty;
                        Console.WriteLine(string.Format(L.UserFound, username, countInfo));
                        failedTracks = await DownloadLikesAsync(userId, clientId, totalLikes);
                    }
                    else if (kind == "playlist")
                    {
                        // Загрузка всех треков из плейлиста или альбома
                        string playlistTitle = doc.RootElement.TryGetProperty("title", out var titleElem) ? titleElem.GetString() ?? L.PlaylistDefaultTitle : L.PlaylistDefaultTitle;
                        Console.WriteLine(string.Format(L.PlaylistFound, playlistTitle));
                        failedTracks = await DownloadPlaylistAsync(doc.RootElement, clientId);
                    }
                    else if (kind == "track")
                    {
                        // Загрузка одиночного трека
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        var track = JsonSerializer.Deserialize<SoundCloudTrack>(json, options);
                        if (track != null)
                        {
                            bool success = await ProcessTrackAsync(track, clientId, 1, 1);
                            if (!success)
                            {
                                failedTracks.Add(track);
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine(string.Format(L.UnsupportedKind, kind));
                    }

                    // Повторная попытка для неудавшихся треков, если такие есть
                    if (failedTracks.Count > 0)
                    {
                        await RetryFailedTracksAsync(failedTracks, clientId);
                    }

                    // Итоговое сообщение об успешном завершении
                    Console.WriteLine();
                    Console.WriteLine(new string('=', 70));
                    Console.WriteLine(string.Format(L.CompletedSummary, downloadFolder));
                    Console.WriteLine(new string('=', 70));
                }
                catch (Exception ex)
                {
                    Console.WriteLine(string.Format(L.ErrorPrefix, ex.Message));
                }
            }

            // Запрос следующего действия: скачать ещё или закрыть программу
            Console.WriteLine();
            Console.WriteLine(L.NextActionHeader);
            Console.WriteLine(L.NextActionContinue);
            Console.WriteLine(L.NextActionExit);
            Console.Write(L.NextActionPrompt);
            string? choice = Console.ReadLine()?.Trim();

            if (choice == "2" || 
                choice?.Equals("exit", StringComparison.OrdinalIgnoreCase) == true || 
                choice?.Equals("quit", StringComparison.OrdinalIgnoreCase) == true || 
                choice?.Equals("q", StringComparison.OrdinalIgnoreCase) == true || 
                choice?.Equals("n", StringComparison.OrdinalIgnoreCase) == true || 
                choice?.Equals("no", StringComparison.OrdinalIgnoreCase) == true || 
                choice?.Equals("нет", StringComparison.OrdinalIgnoreCase) == true || 
                choice?.Equals("н", StringComparison.OrdinalIgnoreCase) == true)
            {
                Console.WriteLine();
                Console.WriteLine(L.Goodbye);
                break;
            }

            Console.WriteLine();
        }
    }

    /// <summary>
    /// Предлагает пользователю повторить скачивание треков, завершившихся ошибкой.
    /// </summary>
    private static async Task RetryFailedTracksAsync(List<SoundCloudTrack> failedTracks, string clientId)
    {
        while (failedTracks.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine(string.Format(L.FailedTracksCount, failedTracks.Count));
            Console.Write(L.RetryPrompt);
            string? input = Console.ReadLine()?.Trim();
            if (input != null && (input.Equals("n", StringComparison.OrdinalIgnoreCase) || input.Equals("no", StringComparison.OrdinalIgnoreCase) || input.Equals("н", StringComparison.OrdinalIgnoreCase) || input.Equals("нет", StringComparison.OrdinalIgnoreCase)))
            {
                break;
            }

            Console.WriteLine();
            Console.WriteLine(L.RetryingFailedTracks);
            var currentBatch = new List<SoundCloudTrack>(failedTracks);
            failedTracks.Clear();

            for (int i = 0; i < currentBatch.Count; i++)
            {
                var track = currentBatch[i];
                bool success = await ProcessTrackAsync(track, clientId, i + 1, currentBatch.Count);
                if (!success)
                {
                    failedTracks.Add(track);
                }
            }
        }
    }

    /// <summary>
    /// Загружает все треки из плейлиста, догружая полные метаданные батчами при необходимости.
    /// </summary>
    private static async Task<List<SoundCloudTrack>> DownloadPlaylistAsync(JsonElement playlistElement, string clientId)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var failedTracks = new List<SoundCloudTrack>();
        if (!playlistElement.TryGetProperty("tracks", out var tracksElement) || tracksElement.ValueKind != JsonValueKind.Array)
        {
            Console.WriteLine(L.PlaylistEmpty);
            return failedTracks;
        }

        var trackIdsToFetch = new List<long>();
        var tracks = new List<SoundCloudTrack>();

        // Проходим по элементам плейлиста: если трек уже содержит media, берем сразу, иначе сохраняем ID для батча
        foreach (var trackItem in tracksElement.EnumerateArray())
        {
            if (trackItem.TryGetProperty("media", out _))
            {
                var track = JsonSerializer.Deserialize<SoundCloudTrack>(trackItem.GetRawText(), options);
                if (track != null) tracks.Add(track);
            }
            else if (trackItem.TryGetProperty("id", out var idElem))
            {
                trackIdsToFetch.Add(idElem.GetInt64());
            }
        }

        // Пакетная загрузка детальной информации о треках (до 50 ID за один запрос)
        if (trackIdsToFetch.Count > 0)
        {
            for (int i = 0; i < trackIdsToFetch.Count; i += 50)
            {
                var batch = trackIdsToFetch.Skip(i).Take(50);
                string idsParam = string.Join("%2C", batch);
                string tracksUrl = $"https://api-v2.soundcloud.com/tracks?ids={idsParam}&client_id={clientId}";
                try
                {
                    string batchJson = await httpClient.GetStringAsync(tracksUrl);
                    using var batchDoc = JsonDocument.Parse(batchJson);
                    foreach (var trackElem in batchDoc.RootElement.EnumerateArray())
                    {
                        var track = JsonSerializer.Deserialize<SoundCloudTrack>(trackElem.GetRawText(), options);
                        if (track != null)
                        {
                            tracks.Add(track);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(string.Format(L.TracksInfoError, ex.Message));
                }
            }
        }

        int totalTracks = tracks.Count;
        int currentTrack = 0;

        // Последовательное скачивание каждого трека из плейлиста
        foreach (var track in tracks)
        {
            currentTrack++;
            bool success = await ProcessTrackAsync(track, clientId, currentTrack, totalTracks);
            if (!success)
            {
                failedTracks.Add(track);
            }
        }

        return failedTracks;
    }

    /// <summary>
    /// Загружает все понравившиеся треки (лайки) пользователя с пагинацией.
    /// </summary>
    private static async Task<List<SoundCloudTrack>> DownloadLikesAsync(long userId, string clientId, int totalLikes)
    {
        string? url = $"https://api-v2.soundcloud.com/users/{userId}/likes?client_id={clientId}&limit=100";
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var failedTracks = new List<SoundCloudTrack>();
        int currentTrack = 0;

        // Цикл постраничной загрузки лайков через next_href
        while (!string.IsNullOrEmpty(url))
        {
            string json = await httpClient.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);

            var collection = doc.RootElement.GetProperty("collection");
            foreach (var item in collection.EnumerateArray())
            {
                if (item.TryGetProperty("track", out var trackElem))
                {
                    var track = JsonSerializer.Deserialize<SoundCloudTrack>(trackElem.GetRawText(), options);
                    if (track != null && track.Media?.Transcodings != null)
                    {
                        currentTrack++;
                        bool success = await ProcessTrackAsync(track, clientId, currentTrack, totalLikes);
                        if (!success)
                        {
                            failedTracks.Add(track);
                        }
                    }
                }
            }

            // Переход на следующую страницу коллекции лайков
            if (doc.RootElement.TryGetProperty("next_href", out var nextHrefElem) && nextHrefElem.ValueKind == JsonValueKind.String)
            {
                url = nextHrefElem.GetString();
                if (url != null && !url.Contains("client_id=")) url += $"&client_id={clientId}";
            }
            else
            {
                url = null;
            }
        }

        return failedTracks;
    }

    /// <summary>
    /// Обрабатывает отдельный трек: находит аудиопоток, проверяет дубликаты, скачивает MP3 и прописывает ID3-теги с обложкой.
    /// </summary>
    private static async Task<bool> ProcessTrackAsync(SoundCloudTrack track, string clientId, int currentTrackIndex, int totalTracks)
    {
        string title = track.Title ?? L.UntitledTrack;
        string artist = track.User?.Username ?? L.UnknownArtist;

        try
        {
            // Поиск подходящего транскодирования (приоритет у progressive формата MP3)
            var mediaTranscoding = track.Media?.Transcodings?
                .FirstOrDefault(t => t.Format?.Protocol == "progressive")
                ?? track.Media?.Transcodings?.FirstOrDefault(t => t.Format?.Protocol == "hls");

            if (mediaTranscoding == null)
            {
                Console.WriteLine(string.Format(L.StreamNotFound, artist, title));
                return false;
            }

            // Получение прямой ссылки на аудиопоток из CDN
            string audioCdnUrl = await GetAudioStreamUrlAsync(mediaTranscoding.Url, clientId, track.TrackAuthorization);

            // Формирование безопасного имени файла и создание целевой папки
            string safeFileName = SanitizeFileName($"{artist} - {title}.mp3");
            string outputDirectory = GetDownloadsDirectory();
            Directory.CreateDirectory(outputDirectory);
            string outputPath = Path.Combine(outputDirectory, safeFileName);

            // Пропуск скачивания, если файл уже существует на диске
            if (System.IO.File.Exists(outputPath))
            {
                string counterStr = totalTracks > 0 ? $"[{currentTrackIndex}/{totalTracks}]" : $"[{currentTrackIndex}]";
                Console.WriteLine(string.Format(L.AlreadyExists, counterStr, artist, title));
                return true;
            }

            // Скачивание файла с динамическим отображением прогресса и цитат
            await DownloadFileWithProgressAsync(audioCdnUrl, outputPath, artist, title, currentTrackIndex, totalTracks);

            // Загрузка обложки в высоком разрешении и встраивание ID3-тегов (название, исполнитель, арт)
            string? artworkUrl = track.ArtworkUrl ?? track.User?.AvatarUrl;
            if (!string.IsNullOrEmpty(artworkUrl))
            {
                try
                {
                    string highResArtwork = artworkUrl.Replace("-large", "-t500x500");
                    byte[] artworkBytes = await httpClient.GetByteArrayAsync(highResArtwork);

                    using var file = TagLib.File.Create(outputPath);
                    var picture = new TagLib.Picture(new TagLib.ByteVector(artworkBytes))
                    {
                        Type = TagLib.PictureType.FrontCover,
                        MimeType = "image/jpeg",
                        Description = "Cover"
                    };
                    file.Tag.Pictures = new TagLib.IPicture[] { picture };
                    file.Tag.Title = title;
                    file.Tag.Performers = new[] { artist };
                    file.Save();
                }
                catch { }
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine(string.Format(L.TrackDownloadError, ex.Message));
            return false;
        }
    }

    /// <summary>
    /// Автоматически извлекает действующий Client ID из JS-бандлов на главной странице SoundCloud.
    /// </summary>
    private static async Task<string> GetClientIdAsync()
    {
        try
        {
            string mainPageHtml = await httpClient.GetStringAsync("https://soundcloud.com");
            var scriptMatches = Regex.Matches(mainPageHtml, @"src=""(https://a-v2\.sndcdn\.com/assets/[^""]+\.js)""");

            foreach (Match match in scriptMatches)
            {
                string scriptUrl = match.Groups[1].Value;
                try
                {
                    string scriptContent = await httpClient.GetStringAsync(scriptUrl);
                    var clientMatch = Regex.Match(scriptContent, @"client_id[:=]""([a-zA-Z0-9]{32})""");
                    if (clientMatch.Success)
                    {
                        return clientMatch.Groups[1].Value;
                    }
                }
                catch { }
            }
        }
        catch { }

        // Запасной Client ID по умолчанию, если автопарсинг не удался
        return "iZ86SZmYVdZgWind2Zwk9TKAKejZnsFl";
    }

    /// <summary>
    /// Получает прямой CDN URL аудиофайла по URL транскодирования и Client ID.
    /// </summary>
    private static async Task<string> GetAudioStreamUrlAsync(string transcodingUrl, string clientId, string? trackAuth)
    {
        string url = $"{transcodingUrl}?client_id={clientId}";
        if (!string.IsNullOrEmpty(trackAuth))
        {
            url += $"&track_authorization={Uri.EscapeDataString(trackAuth)}";
        }

        string json = await httpClient.GetStringAsync(url);
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("url", out var urlElement))
        {
            return urlElement.GetString() ?? throw new Exception(L.EmptyAudioUrlError);
        }

        throw new Exception(L.StreamUrlExtractError);
    }

    /// <summary>
    /// Скачивает аудиофайл с отображением заголовка трека, прогресс-бара, размера и циклических цитат.
    /// </summary>
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
        using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        byte[] buffer = new byte[8192];
        long totalBytesRead = 0;
        int bytesRead;

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        const int totalLines = 4;
        bool canUseCursor = false;
        int startTop = 0;

        // Выделение 4 строк в консоли для отрисовки интерактивного интерфейса
        try
        {
            if (!Console.IsOutputRedirected)
            {
                for (int i = 0; i < totalLines; i++) Console.WriteLine();
                startTop = Console.CursorTop - totalLines;
                canUseCursor = true;
            }
        }
        catch
        {
            canUseCursor = false;
        }

        long lastRenderTicks = 0;
        string countStr = totalTracks > 0 ? $"{currentTrackIndex}/{totalTracks}" : $"{currentTrackIndex}";
        string trackHeader = string.Format(L.DownloadingHeader, countStr, artist, title);

        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, bytesRead);
            totalBytesRead += bytesRead;

            // Ограничение частоты перерисовки (не чаще раза в 40 мс) для плавности и экономии CPU
            long currentTicks = stopwatch.ElapsedMilliseconds;
            if (currentTicks - lastRenderTicks < 40 && bytesRead > 0 && totalBytesRead < totalBytes)
            {
                continue;
            }
            lastRenderTicks = currentTicks;

            // Смена цитаты каждые 2 секунды (2000 мс)
            int quoteIndex = (int)((currentTicks / 2000) % L.Quotes.Length);
            string currentQuote = L.Quotes[quoteIndex];

            // Расчёт процентов и блочного прогресс-бара длиной 48 символов
            double percentage = totalBytes > 0 ? (double)totalBytesRead / totalBytes * 100 : 0;
            int barLength = 48;
            int filledLength = Math.Clamp((int)(percentage / 100 * barLength), 0, barLength);
            string bar = new string('█', filledLength) + new string('░', barLength - filledLength);

            string progressBarLine = totalBytes > 0
                ? $"   [{bar}] {percentage:F1}%"
                : $"   [{bar}] {totalBytesRead / 1024.0 / 1024.0:F2} MB";

            string statsLine = totalBytes > 0
                ? (totalTracks > 0
                    ? string.Format(L.StatsSize, totalBytesRead / 1024.0 / 1024.0, totalBytes / 1024.0 / 1024.0, currentTrackIndex, totalTracks)
                    : string.Format(L.StatsSizeSingle, totalBytesRead / 1024.0 / 1024.0, totalBytes / 1024.0 / 1024.0))
                : string.Format(L.StatsDownloaded, totalBytesRead / 1024.0 / 1024.0);

            string quoteLine = $"   💬 «{currentQuote}»";

            // Отрисовка строк по фиксированным позициям курсора
            if (canUseCursor)
            {
                try
                {
                    int windowWidth = Math.Max(Console.WindowWidth, 80);

                    // Строка 0: Заголовок и имя трека
                    Console.SetCursorPosition(0, startTop);
                    Console.Write(FormatLine(trackHeader, windowWidth - 1));

                    // Строка 1: Прогресс-бар
                    Console.SetCursorPosition(0, startTop + 1);
                    Console.Write(FormatLine(progressBarLine, windowWidth - 1));

                    // Строка 2: Статистика мегабайт и номер трека
                    Console.SetCursorPosition(0, startTop + 2);
                    Console.Write(FormatLine(statsLine, windowWidth - 1));

                    // Строка 3: Сменяющаяся цитата
                    Console.SetCursorPosition(0, startTop + 3);
                    Console.Write(FormatLine(quoteLine, windowWidth - 1));
                }
                catch
                {
                    canUseCursor = false;
                    Console.Write($"\r{progressBarLine} | {statsLine} | «{currentQuote}»   ");
                }
            }
            else
            {
                Console.Write($"\r{progressBarLine} | {statsLine} | «{currentQuote}»   ");
            }
        }

        // Отрисовка финального состояния после успешной загрузки
        if (canUseCursor)
        {
            try
            {
                int windowWidth = Math.Max(Console.WindowWidth, 80);
                string completedBar = new string('█', 48);
                string finalBar = $"   [{completedBar}] 100.0%";
                string finalStats = totalBytes > 0 
                    ? string.Format(L.CompletedStats, totalBytes / 1024.0 / 1024.0) 
                    : string.Format(L.CompletedStats, totalBytesRead / 1024.0 / 1024.0);

                Console.SetCursorPosition(0, startTop + 1);
                Console.Write(FormatLine(finalBar, windowWidth - 1));

                Console.SetCursorPosition(0, startTop + 2);
                Console.Write(FormatLine(finalStats, windowWidth - 1));

                Console.SetCursorPosition(0, startTop + 3);
                Console.Write(new string(' ', windowWidth - 1));

                Console.SetCursorPosition(0, startTop + 4);
            }
            catch
            {
                Console.WriteLine();
            }
        }
        else
        {
            Console.WriteLine();
        }
    }

    /// <summary>
    /// Безопасно форматирует и обрезает строку под ширину окна консоли, предотвращая перенос строк и сдвиг курсора.
    /// </summary>
    private static string FormatLine(string text, int maxWidth)
    {
        if (maxWidth <= 0) return text;
        if (text.Length > maxWidth)
        {
            return text.Substring(0, Math.Max(0, maxWidth - 3)) + "...";
        }
        return text.PadRight(maxWidth);
    }

    /// <summary>
    /// Удаляет недопустимые для файловой системы символы, концевые точки и пробелы из имени файла.
    /// </summary>
    private static string SanitizeFileName(string fileName)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(c, '_');
        }
        return fileName.Trim().TrimEnd('.');
    }

    /// <summary>
    /// Возвращает кроссплатформенный путь к папке «Загрузки» (Downloads).
    /// </summary>
    private static string GetDownloadsDirectory()
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            userProfile = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        }
        return Path.Combine(userProfile, "Downloads");
    }
}

/// <summary>
/// Контейнер строк локализации для поддержки многоязычности (RU/EN).
/// </summary>
public class LocalizedStrings
{
    public required string HeaderTitle { get; init; }
    public required string QuickGuideHeader { get; init; }
    public required string QuickGuideTrack { get; init; }
    public required string QuickGuidePlaylist { get; init; }
    public required string QuickGuideUser { get; init; }
    public required string SaveLocationInfo { get; init; }
    public required string UrlPrompt { get; init; }
    public required string ErrorEmptyUrl { get; init; }
    public required string Step1ClientId { get; init; }
    public required string Step2Resolve { get; init; }
    public required string UserFound { get; init; }
    public required string TotalApprox { get; init; }
    public required string PlaylistFound { get; init; }
    public required string PlaylistDefaultTitle { get; init; }
    public required string UnknownUser { get; init; }
    public required string UnknownArtist { get; init; }
    public required string UntitledTrack { get; init; }
    public required string PlaylistEmpty { get; init; }
    public required string TrackProcessError { get; init; }
    public required string UnsupportedKind { get; init; }
    public required string CompletedSummary { get; init; }
    public required string ErrorPrefix { get; init; }
    public required string TracksInfoError { get; init; }
    public required string TrackDownloadError { get; init; }
    public required string StreamNotFound { get; init; }
    public required string AlreadyExists { get; init; }
    public required string DownloadingHeader { get; init; }
    public required string StatsSize { get; init; }
    public required string StatsSizeSingle { get; init; }
    public required string StatsDownloaded { get; init; }
    public required string CompletedStats { get; init; }
    public required string FailedTracksCount { get; init; }
    public required string RetryPrompt { get; init; }
    public required string RetryingFailedTracks { get; init; }
    public required string NextActionHeader { get; init; }
    public required string NextActionContinue { get; init; }
    public required string NextActionExit { get; init; }
    public required string NextActionPrompt { get; init; }
    public required string Goodbye { get; init; }
    public required string EmptyAudioUrlError { get; init; }
    public required string StreamUrlExtractError { get; init; }
    public required string[] Quotes { get; init; }
}

/// <summary>
/// DTO модель трека SoundCloud.
/// </summary>
public class SoundCloudTrack
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("duration")]
    public long? DurationMs { get; set; }

    [JsonPropertyName("artwork_url")]
    public string? ArtworkUrl { get; set; }

    [JsonPropertyName("track_authorization")]
    public string? TrackAuthorization { get; set; }

    [JsonPropertyName("user")]
    public SoundCloudUser? User { get; set; }

    [JsonPropertyName("media")]
    public SoundCloudMedia? Media { get; set; }
}

/// <summary>
/// DTO модель автора/пользователя SoundCloud.
/// </summary>
public class SoundCloudUser
{
    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("avatar_url")]
    public string? AvatarUrl { get; set; }
}

/// <summary>
/// DTO модель медиа-контейнера трека.
/// </summary>
public class SoundCloudMedia
{
    [JsonPropertyName("transcodings")]
    public Transcoding[]? Transcodings { get; set; }
}

/// <summary>
/// DTO модель отдельного аудиопотока (транскодирования).
/// </summary>
public class Transcoding
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("preset")]
    public string? Preset { get; set; }

    [JsonPropertyName("format")]
    public TranscodingFormat? Format { get; set; }
}

/// <summary>
/// DTO модель формата и протокола аудиопотока.
/// </summary>
public class TranscodingFormat
{
    [JsonPropertyName("protocol")]
    public string Protocol { get; set; } = string.Empty;

    [JsonPropertyName("mime_type")]
    public string? MimeType { get; set; }
}