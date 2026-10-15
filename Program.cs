using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using TagLib;

namespace SoundCloudDownloader;

/// <summary>
/// Главный класс консольного загрузчика треков, плейлистов и лайков с SoundCloud.
/// </summary>
public class Program
{
    // Единственный переиспользуемый HTTP-клиент с таймаутом 30с и User-Agent браузера (для обхода базовой защиты от ботов).
    private static readonly HttpClient httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
        DefaultRequestHeaders =
        {
            { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36" }
        }
    };

    // Настройки JSON: регистронезависимое сопоставление свойств DTO (SoundCloud использует snake_case и camelCase).
    internal static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    // Флаг языка интерфейса: по умолчанию определяется язык текущей локали системы (русский или английский).
    internal static bool IsRu = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ru", StringComparison.OrdinalIgnoreCase);

    // Кэш Client ID: получается один раз за время жизни процесса и повторно используется для всех запросов к API.
    private static string? _cachedClientId;

    /// <summary>
    /// Возвращает строку на выбранном языке (RU/EN).
    /// </summary>
    internal static string L(string ru, string en) => IsRu ? ru : en;

    static async Task Main(string[] args)
    {
        // Устанавливаем UTF-8 в консоли для корректного отображения кириллицы, спецсимволов и emoji.
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // Если ссылка была передана первым аргументом командной строки: dotnet run -- "<url>"
        string initialArgUrl = args.Length > 0 ? args[0] : string.Empty;

        // Если запущено без аргументов — предлагаем пользователю выбрать язык интерфейса.
        if (string.IsNullOrWhiteSpace(initialArgUrl))
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

        string downloadFolder = GetDownloadsDirectory();

        // Главный интерактивный цикл программы.
        while (true)
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

            // Считывание ссылки: из аргумента при первом запуске либо из ввода пользователя.
            string trackUrl;
            if (!string.IsNullOrWhiteSpace(initialArgUrl))
            {
                trackUrl = initialArgUrl;
                initialArgUrl = string.Empty; // Сбрасываем аргумент, чтобы при следующем круге запросить ввод.
                Console.WriteLine(L($"Введите ссылку на трек, плейлист или пользователя SoundCloud: {trackUrl}",
                                    $"Enter SoundCloud track, playlist, or user profile link: {trackUrl}"));
            }
            else
            {
                Console.Write(L("Введите ссылку на трек, плейлист или пользователя SoundCloud: ",
                                "Enter SoundCloud track, playlist, or user profile link: "));
                trackUrl = Console.ReadLine()?.Trim() ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(trackUrl))
            {
                Console.WriteLine(L("Ошибка: ссылка не была указана.", "Error: No link was provided."));
            }
            else
            {
                try
                {
                    // Шаг 1: Получение действующего Client ID для запросов к закрытому v2 API.
                    Console.WriteLine(L("1. Получение Client ID...", "1. Fetching Client ID..."));
                    string clientId = await GetClientIdAsync();
                    Console.WriteLine($"   Client ID: {clientId}");

                    // Нормализация ссылки (удаление завершающего /likes, концевых слешей).
                    trackUrl = NormalizeSoundCloudUrl(trackUrl);

                    // Шаг 2: Обращение к /resolve эндпоинту API для определения типа объекта по веб-ссылке.
                    Console.WriteLine(L("2. Получение информации по ссылке...", "2. Resolving URL..."));
                    string resolveUrl = $"https://api-v2.soundcloud.com/resolve?url={Uri.EscapeDataString(trackUrl)}&client_id={clientId}";
                    string json = await httpClient.GetStringAsync(resolveUrl);
                    using var doc = JsonDocument.Parse(json);

                    // Свойство "kind" указывает на тип сущности: user, playlist или track.
                    string kind = doc.RootElement.GetProperty("kind").GetString() ?? string.Empty;
                    var failedTracks = new List<SoundCloudTrack>();

                    // 1. Профиль пользователя -> скачиваем все лайки (понравившиеся треки).
                    if (kind == "user")
                    {
                        long userId = doc.RootElement.GetProperty("id").GetInt64();
                        string username = doc.RootElement.TryGetProperty("username", out var u) && u.GetString() is { Length: > 0 } name
                            ? name
                            : L("Неизвестный пользователь", "Unknown user");

                        // Попытка извлечь общее количество лайков для отображения счетчика [x/y].
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
                        failedTracks = await DownloadLikesAsync(userId, clientId, totalLikes);
                    }
                    // 2. Плейлист или альбом -> скачиваем все включенные треки.
                    else if (kind == "playlist")
                    {
                        string title = doc.RootElement.TryGetProperty("title", out var t) && t.GetString() is { Length: > 0 } playlistTitle
                            ? playlistTitle
                            : L("Плейлист", "Playlist");

                        Console.WriteLine(L($"   Плейлист: {title}. Загрузка треков плейлиста...",
                                            $"   Playlist: {title}. Downloading playlist tracks..."));
                        failedTracks = await DownloadPlaylistAsync(doc.RootElement, clientId);
                    }
                    // 3. Отдельный трек -> прямое скачивание.
                    else if (kind == "track")
                    {
                        var track = doc.RootElement.Deserialize<SoundCloudTrack>(JsonOptions);
                        if (track != null)
                        {
                            bool success = await ProcessTrackAsync(track, clientId, 1, 1);
                            if (!success) failedTracks.Add(track);
                        }
                    }
                    else
                    {
                        Console.WriteLine(L($"Неподдерживаемый тип ссылки: {kind}. Поддерживаются треки, плейлисты и профили пользователей.",
                                            $"Unsupported link type: {kind}. Supported: tracks, playlists, and user profiles."));
                    }

                    // Если какие-либо треки не удалось скачать (сбой сети, таймаут) — предлагаем повтор.
                    if (failedTracks.Count > 0)
                    {
                        await RetryFailedTracksAsync(failedTracks, clientId);
                    }

                    Console.WriteLine();
                    Console.WriteLine(new string('=', 70));
                    Console.WriteLine(L($"Загрузка завершена! Файлы сохранены в: {downloadFolder}",
                                        $"Download finished! Files saved to: {downloadFolder}"));
                    Console.WriteLine(new string('=', 70));
                }
                catch (Exception ex)
                {
                    Console.WriteLine(L($"\n[Ошибка]: {ex.Message}", $"\n[Error]: {ex.Message}"));
                }
            }

            // Запрос дальнейших действий у пользователя.
            Console.WriteLine();
            Console.WriteLine(L("Что вы хотите сделать дальше?", "What would you like to do next?"));
            Console.WriteLine(L("  1. Скачать ещё (продолжить)", "  1. Download more (continue)"));
            Console.WriteLine(L("  2. Выйти из программы", "  2. Exit program"));
            Console.Write(L("Выберите действие [1/2] (по умолчанию: 1): ", "Choose an option [1/2] (Default: 1): "));
            string? choice = Console.ReadLine()?.Trim();

            // Завершаем цикл, если пользователь выбрал пункт 2 или ввел команду выхода.
            if (choice is "2" or "q" or "quit" or "exit" or "n" or "no" or "нет" or "н")
            {
                Console.WriteLine();
                Console.WriteLine(L("Завершение работы. До свидания!", "Exiting. Goodbye!"));
                break;
            }

            Console.WriteLine();
        }
    }

    /// <summary>
    /// Нормализует URL SoundCloud. Если пользователь передал ссылку на вкладку /likes (напр. soundcloud.com/user/likes),
    /// обрезает /likes, так как эндпоинт /resolve ожидает базовый URL профиля.
    /// </summary>
    internal static string NormalizeSoundCloudUrl(string url)
    {
        url = url.Trim().TrimEnd('/');
        if (url.EndsWith("/likes", StringComparison.OrdinalIgnoreCase))
        {
            url = url[..^6].TrimEnd('/');
        }
        return url;
    }

    /// <summary>
    /// Предлагает пользователю повторить попытку скачивания для треков, завершившихся ошибкой.
    /// </summary>
    private static async Task RetryFailedTracksAsync(List<SoundCloudTrack> failedTracks, string clientId)
    {
        while (failedTracks.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine(L($"Не удалось скачать треков: {failedTracks.Count}", $"Failed to download {failedTracks.Count} track(s)."));
            Console.Write(L("Попробовать скачать неудавшиеся треки снова? [Y/n] (по умолчанию: Да): ", "Retry downloading failed tracks? [Y/n] (default: Yes): "));
            string? input = Console.ReadLine()?.Trim();
            if (input is "n" or "no" or "н" or "нет") break;

            Console.WriteLine();
            Console.WriteLine(L("Повторная попытка загрузки треков...", "Retrying failed tracks..."));
            var currentBatch = new List<SoundCloudTrack>(failedTracks);
            failedTracks.Clear();

            for (int i = 0; i < currentBatch.Count; i++)
            {
                var track = currentBatch[i];
                bool success = await ProcessTrackAsync(track, clientId, i + 1, currentBatch.Count);
                if (!success) failedTracks.Add(track);
            }
        }
    }

    /// <summary>
    /// Скачивает все треки из плейлиста. В ответе SoundCloud часть треков содержит полные данные,
    /// а часть (хвост плейлиста) представлена только идентификаторами ID. Метод пачками дозагружает недостающие данные.
    /// </summary>
    private static async Task<List<SoundCloudTrack>> DownloadPlaylistAsync(JsonElement playlistElement, string clientId)
    {
        var failedTracks = new List<SoundCloudTrack>();
        if (!playlistElement.TryGetProperty("tracks", out var tracksElement) || tracksElement.ValueKind != JsonValueKind.Array)
        {
            Console.WriteLine(L("   Плейлист пуст или не содержит треков.", "   Playlist is empty or contains no tracks."));
            return failedTracks;
        }

        var trackIdsToFetch = new List<long>();
        var tracks = new List<SoundCloudTrack>();

        // Разделяем треки: готовые с объектом media и неполные (только ID).
        foreach (var trackItem in tracksElement.EnumerateArray())
        {
            if (trackItem.TryGetProperty("media", out _))
            {
                var track = trackItem.Deserialize<SoundCloudTrack>(JsonOptions);
                if (track != null) tracks.Add(track);
            }
            else if (trackItem.TryGetProperty("id", out var idElem))
            {
                trackIdsToFetch.Add(idElem.GetInt64());
            }
        }

        // Если есть треки только с ID — запрашиваем их полные данные пачками по 50 штук (ограничение API SoundCloud).
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
                        var track = trackElem.Deserialize<SoundCloudTrack>(JsonOptions);
                        if (track != null) tracks.Add(track);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(L($"   ! Ошибка при получении информации о треках: {ex.Message}",
                                        $"   ! Error fetching tracks information: {ex.Message}"));
                }
            }
        }

        // Последовательно скачиваем каждый трек плейлиста.
        for (int i = 0; i < tracks.Count; i++)
        {
            bool success = await ProcessTrackAsync(tracks[i], clientId, i + 1, tracks.Count);
            if (!success) failedTracks.Add(tracks[i]);
        }

        return failedTracks;
    }

    /// <summary>
    /// Постранично выгружает все лайки пользователя через эндпоинт /users/{id}/likes (курсор next_href).
    /// </summary>
    private static async Task<List<SoundCloudTrack>> DownloadLikesAsync(long userId, string clientId, int totalLikes)
    {
        // Начальный URL с лимитом 100 треков на страницу.
        string? url = $"https://api-v2.soundcloud.com/users/{userId}/likes?client_id={clientId}&limit=100";
        var failedTracks = new List<SoundCloudTrack>();
        int currentTrack = 0;

        // Идем по ссылкам пагинации next_href, пока они возвращаются API.
        while (!string.IsNullOrEmpty(url))
        {
            string json;
            try
            {
                json = await httpClient.GetStringAsync(url);
            }
            catch (Exception ex)
            {
                Console.WriteLine(L($"   ! Ошибка загрузки списка лайков: {ex.Message}",
                                    $"   ! Error loading likes list: {ex.Message}"));
                break;
            }

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("collection", out var collection) || collection.ValueKind != JsonValueKind.Array)
            {
                break;
            }

            // В коллекции лайков трек находится во вложенном объекте "track".
            foreach (var item in collection.EnumerateArray())
            {
                if (item.TryGetProperty("track", out var trackElem))
                {
                    var track = trackElem.Deserialize<SoundCloudTrack>(JsonOptions);
                    if (track?.Media?.Transcodings != null)
                    {
                        currentTrack++;
                        bool success = await ProcessTrackAsync(track, clientId, currentTrack, totalLikes);
                        if (!success) failedTracks.Add(track);
                    }
                }
            }

            // Получаем ссылку на следующую страницу (next_href) и проверяем наличие client_id.
            if (doc.RootElement.TryGetProperty("next_href", out var nextHrefElem) && nextHrefElem.ValueKind == JsonValueKind.String)
            {
                url = nextHrefElem.GetString();
                if (url != null && !url.Contains("client_id=")) url += $"&client_id={clientId}";
            }
            else
            {
                url = null; // Страницы закончились.
            }
        }

        return failedTracks;
    }

    /// <summary>
    /// Выполняет полный цикл обработки одного трека:
    /// 1. Проверяет, не скачан ли файл ранее.
    /// 2. Находит прямой progressive MP3 поток.
    /// 3. Скачивает поток во временный файл .tmp (для защиты от обрыва соединения).
    /// 4. Встраивает ID3-теги (название, артист) и обложку 500x500 через TagLibSharp.
    /// 5. Атомарно переименовывает .tmp в .mp3.
    /// </summary>
    internal static async Task<bool> ProcessTrackAsync(SoundCloudTrack track, string clientId, int currentTrackIndex, int totalTracks)
    {
        string title = string.IsNullOrWhiteSpace(track.Title) ? L("Без названия", "Untitled") : track.Title.Trim();
        string artist = string.IsNullOrWhiteSpace(track.User?.Username) ? L("Неизвестный исполнитель", "Unknown artist") : track.User.Username.Trim();

        // Очищаем имя файла от запрещенных символов Windows/Linux.
        string safeFileName = SanitizeFileName($"{artist} - {title}.mp3");
        string outputDirectory = GetDownloadsDirectory();
        Directory.CreateDirectory(outputDirectory);
        string outputPath = Path.Combine(outputDirectory, safeFileName);
        string tempPath = outputPath + ".tmp";

        try
        {
            // Пропуск трека, если он уже полностью скачан ранее.
            if (System.IO.File.Exists(outputPath))
            {
                string counterStr = totalTracks > 0 ? $"[{currentTrackIndex}/{totalTracks}]" : $"[{currentTrackIndex}]";
                Console.WriteLine(L($"   ✓ {counterStr} Уже существует: {artist} - {title}",
                                    $"   ✓ {counterStr} Already exists: {artist} - {title}"));
                return true;
            }

            // Ищем именно progressive MP3 аудиопоток.
            // HLS (m3u8) не скачивается напрямую, так как это текстовый плейлист сегментов, приводящий к битым файлам без ffmpeg.
            var mediaTranscoding = track.Media?.Transcodings?
                .FirstOrDefault(t => t.Format?.Protocol == "progressive");

            if (mediaTranscoding == null)
            {
                Console.WriteLine(L(
                    $"   [!] {artist} - {title}: подходящий прогрессивный MP3 поток не найден (доступен только защищённый HLS поток).",
                    $"   [!] {artist} - {title}: suitable progressive MP3 stream not found (HLS only)."
                ));
                return false;
            }

            // Запрашиваем прямую ссылку на аудиофайл в CDN SoundCloud.
            string audioCdnUrl = await GetAudioStreamUrlAsync(mediaTranscoding.Url, clientId, track.TrackAuthorization);

            // Потоковая загрузка аудиофайла во временный файл с отображением прогресс-бара.
            await DownloadFileWithProgressAsync(audioCdnUrl, tempPath, artist, title, currentTrackIndex, totalTracks);

            // Встраивание метаданных ID3v2 (исполнитель, название) и обложки через TagLibSharp.
            try
            {
                using var file = TagLib.File.Create(tempPath);
                file.Tag.Title = title;
                file.Tag.Performers = new[] { artist };

                // Обложка трека либо аватар исполнителя при отсутствии обложки.
                string? artworkUrl = track.ArtworkUrl ?? track.User?.AvatarUrl;
                if (!string.IsNullOrEmpty(artworkUrl))
                {
                    try
                    {
                        // SoundCloud по умолчанию отдает миниатюру 100x100 (-large). Заменяем на высокое разрешение 500x500 (-t500x500).
                        string highResArtwork = artworkUrl.Replace("-large", "-t500x500");
                        byte[] artworkBytes = await httpClient.GetByteArrayAsync(highResArtwork);

                        file.Tag.Pictures = new TagLib.IPicture[]
                        {
                            new TagLib.Picture(new TagLib.ByteVector(artworkBytes))
                            {
                                Type = TagLib.PictureType.FrontCover,
                                MimeType = "image/jpeg",
                                Description = "Cover"
                            }
                        };
                    }
                    catch
                    {
                        // Ошибка загрузки обложки не должна препятствовать сохранению трека.
                    }
                }

                file.Save();
            }
            catch
            {
                // Ошибка записи ID3 не должна препятствовать сохранению самого аудиофайла.
            }

            // Атомарно переименовываем проверенный и размеченный временный файл в итоговый .mp3.
            System.IO.File.Move(tempPath, outputPath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            // При ошибке или обрыве соединения удаляем незавершенный временный файл.
            if (System.IO.File.Exists(tempPath))
            {
                try { System.IO.File.Delete(tempPath); } catch { }
            }

            Console.WriteLine(L($"   ! Ошибка при скачивании трека: {ex.Message}",
                                $"   ! Error downloading track: {ex.Message}"));
            return false;
        }
    }

    /// <summary>
    /// Динамически находит действующий Client ID в JS-скриптах на soundcloud.com.
    /// Перебирает скрипты в обратном порядке (Reverse), так как основной бандл с ключом находится в конце HTML.
    /// </summary>
    internal static async Task<string> GetClientIdAsync()
    {
        // Возвращаем кэшированное значение, если оно уже было найдено ранее.
        if (!string.IsNullOrEmpty(_cachedClientId)) return _cachedClientId;

        try
        {
            string mainPageHtml = await httpClient.GetStringAsync("https://soundcloud.com");
            var scriptMatches = Regex.Matches(mainPageHtml, @"src=[""'](https://[a-zA-Z0-9\-]+\.sndcdn\.com/assets/[^""']+\.js)[""']");

            // Ищем с конца: SoundCloud всегда подключает бандл приложения в последних тегах <script>.
            // Это ускоряет поиск с 15-30 секунд до < 1 секунды.
            foreach (Match match in scriptMatches.Cast<Match>().Reverse())
            {
                try
                {
                    string scriptContent = await httpClient.GetStringAsync(match.Groups[1].Value);
                    var clientMatch = Regex.Match(scriptContent, @"client_id[:=][""']([a-zA-Z0-9]{32})[""']");
                    if (clientMatch.Success)
                    {
                        return _cachedClientId = clientMatch.Groups[1].Value;
                    }
                }
                catch
                {
                    // Игнорируем сетевые сбои на отдельных JS-файлах и переходим к следующему.
                }
            }
        }
        catch { }

        throw new InvalidOperationException(L(
            "Не удалось получить действующий Client ID с SoundCloud. Проверьте сетевое подключение.",
            "Failed to retrieve an active SoundCloud Client ID. Check your network connection."
        ));
    }

    /// <summary>
    /// Вызывает транскодирующий эндпоинт SoundCloud API для получения прямой временной ссылки на MP3 файл в CDN.
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

        // API возвращает объект {"url": "https://cf-media.sndcdn.com/..."}.
        if (doc.RootElement.TryGetProperty("url", out var urlElement))
        {
            return urlElement.GetString() ?? throw new Exception(L("Получен пустой URL аудиопотока.", "Received an empty audio stream URL."));
        }

        throw new Exception(L("Не удалось извлечь URL потока из ответа API.", "Failed to extract stream URL from API response."));
    }

    /// <summary>
    /// Скачивает файл потоком блоками по 16 КБ, обновляя прогресс-бар в консоли без лишней нагрузки на ЦП.
    /// </summary>
    private static async Task DownloadFileWithProgressAsync(
        string downloadUrl,
        string destinationPath,
        string artist,
        string title,
        int currentTrackIndex,
        int totalTracks)
    {
        // ResponseHeadersRead позволяет начать чтение потока сразу, не загружая весь файл в RAM.
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

            // Ограничиваем частоту перерисовки консоли (не чаще одного раза в 80 мс), чтобы избежать мерцания.
            long elapsed = stopwatch.ElapsedMilliseconds;
            if (elapsed - lastRenderTicks > 80 || totalBytesRead == totalBytes)
            {
                lastRenderTicks = elapsed;
                double mbRead = totalBytesRead / 1048576.0;

                if (totalBytes > 0)
                {
                    double percentage = (double)totalBytesRead / totalBytes * 100.0;
                    double mbTotal = totalBytes / 1048576.0;
                    int filled = Math.Clamp((int)(percentage / 100.0 * 30), 0, 30);
                    string bar = new string('█', filled) + new string('░', 30 - filled);
                    Console.Write($"\r   [{bar}] {percentage,5:F1}%  {mbRead:F2} / {mbTotal:F2} MB");
                }
                else
                {
                    // Если заголовок Content-Length не передан сервером, выводим только количество скачанных МБ.
                    Console.Write($"\r   {mbRead:F2} MB");
                }
            }
        }

        Console.WriteLine(L("  ✓ Завершено", "  ✓ Completed"));
    }

    /// <summary>
    /// Очищает имя файла от символов, запрещенных файловыми системами (Windows, Linux, macOS),
    /// и обрезает слишком длинные имена во избежание превышения лимита MAX_PATH.
    /// </summary>
    internal static string SanitizeFileName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();

        // Заменяем все запрещенные символы на символ подчеркивания '_'.
        string clean = string.Create(name.Length, (name, invalidChars), (span, state) =>
        {
            for (int i = 0; i < state.name.Length; i++)
            {
                char c = state.name[i];
                span[i] = state.invalidChars.Contains(c) ? '_' : c;
            }
        }).Trim(' ', '.');

        // Ограничиваем длину имени файла до 180 символов с сохранением расширения (защита от MAX_PATH в Windows).
        const int maxLen = 180;
        if (clean.Length > maxLen)
        {
            string ext = Path.GetExtension(clean);
            string baseName = Path.GetFileNameWithoutExtension(clean);
            int availableBase = maxLen - ext.Length;
            if (availableBase > 0 && baseName.Length > availableBase)
            {
                clean = baseName[..availableBase].TrimEnd() + ext;
            }
        }

        // Если после очистки имя пустое — генерируем уникальное имя по таймстампу.
        return string.IsNullOrWhiteSpace(clean) || clean.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
            ? $"track_{DateTime.UtcNow.Ticks}.mp3"
            : clean;
    }

    /// <summary>
    /// Кроссплатформенно возвращает абсолютный путь к папке «Загрузки» текущего пользователя ОС.
    /// </summary>
    internal static string GetDownloadsDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
}

// -------------------------------------------------------------------------------------------------
// DTO-модели данных для строго типизированной десериализации JSON-ответов SoundCloud v2 API.
// -------------------------------------------------------------------------------------------------

/// <summary>Модель трека SoundCloud.</summary>
public record SoundCloudTrack(
    string? Title,
    [property: JsonPropertyName("artwork_url")] string? ArtworkUrl,
    [property: JsonPropertyName("track_authorization")] string? TrackAuthorization,
    SoundCloudUser? User,
    SoundCloudMedia? Media
);

/// <summary>Модель пользователя (автора трека).</summary>
public record SoundCloudUser(
    string? Username,
    [property: JsonPropertyName("avatar_url")] string? AvatarUrl
);

/// <summary>Контейнер доступных медиа-потоков трека.</summary>
public record SoundCloudMedia(Transcoding[]? Transcodings);

/// <summary>Информация об отдельном медиа-потоке (URL транскодинга и формат).</summary>
public record Transcoding(
    string Url,
    TranscodingFormat? Format
);

/// <summary>Формат протокола медиа-потока: "progressive" (прямой MP3) или "hls" (сегментированный).</summary>
public record TranscodingFormat(string Protocol);