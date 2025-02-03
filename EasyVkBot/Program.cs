using System.Data.SQLite;
using VkNet;
using VkNet.Enums.Filters;
using VkNet.Model;
using Microsoft.Extensions.DependencyInjection;
using VkNet.Abstractions;

namespace VkBot
{
    /// <summary>
    /// Бот для социальной сети ВКонтакте.
    /// </summary>
    public class VkBot
    {
        private readonly VkApi _api;
        private readonly Random _random = new Random();
        private readonly string _dbPath = "height_data.db"; // Путь к файлу базы данных

        /// <summary>
        /// Конструктор бота.
        /// </summary>
        /// <param name="accessToken">Токен доступа к API ВКонтакте.</param>
        public VkBot(string accessToken)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IVkApi>(_api);
            _api = new VkApi(services);
            _api.Authorize(new ApiAuthParams
            {
                AccessToken = accessToken,
                Settings = Settings.Messages // Указываем, что нужны права на доступ к сообщениям
            });
            InitializeDatabase();
        }

        /// <summary>
        /// Инициализирует базу данных.
        /// </summary>
        private void InitializeDatabase()
        {
            if (!System.IO.File.Exists(_dbPath))
            {
                SQLiteConnection.CreateFile(_dbPath);
            }

            using (var connection = new SQLiteConnection($"Data Source={_dbPath};Version=3;"))
            {
                connection.Open();
                var command = new SQLiteCommand(
                    "CREATE TABLE IF NOT EXISTS HeightData (" +
                    "   peer_id INTEGER," +
                    "   user_id INTEGER," +
                    "   height INTEGER," +
                    "   last_used TEXT," + // Используем TEXT для хранения даты и времени
                    "   PRIMARY KEY (peer_id, user_id)" +
                    ")", connection);
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Запускает бота в асинхронном режиме.
        /// </summary>
        /// <param name="cancellationToken">Токен для отмены выполнения.</param>
        /// <returns>Задача, представляющая асинхронную операцию.</returns>
        public async Task RunAsync(CancellationToken cancellationToken)
        {
            // Получаем информацию о LongPoll сервере
            var longPollServer = await _api.Messages.GetLongPollServerAsync(needPts: true);
            var server = longPollServer.Server;
            var key = longPollServer.Key;
            var ts = longPollServer.Ts;
            ulong? pts = longPollServer.Pts;

            Console.WriteLine("Bot started.");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Запрашиваем обновления с LongPoll сервера
                    var longPollResponse = await _api.Messages.GetLongPollHistoryAsync(new MessagesGetLongPollHistoryParams
                    {
                        Ts = ts,
                        Pts = pts
                    });

                    ts = longPollResponse.NewPts;
                    pts = longPollResponse.NewPts;

                    // Обрабатываем новые сообщения
                    foreach (var message in longPollResponse.Messages)
                    {

                        _ = HandleMessageAsync(message, cancellationToken); // Обрабатываем сообщение асинхронно, не дожидаясь завершения
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                    // Можно добавить логирование ошибок и повторную попытку подключения через некоторое время
                    await Task.Delay(1000, cancellationToken); // Ждем секунду перед повторной попыткой
                    longPollServer = await _api.Messages.GetLongPollServerAsync(needPts: true);
                    ts = longPollServer.Ts;
                    pts = longPollServer.Pts;
                }

                await Task.Delay(100, cancellationToken); // Небольшая задержка, чтобы не перегружать сервер
            }
        }

        /// <summary>
        /// Обрабатывает входящее сообщение.
        /// </summary>
        /// <param name="message">Входящее сообщение.</param>
        /// <param name="cancellationToken">Токен для отмены выполнения.</param>
        /// <returns>Задача, представляющая асинхронную операцию.</returns>
        private async Task HandleMessageAsync(VkNet.Model.Message message, CancellationToken cancellationToken)
        {
            if (message.Text.StartsWith("/"))
            {
                var command = message.Text.ToLower().Split(' ')[0];
                switch (command)
                {
                    case "/рост":
                        await HandleHeightCommandAsync(message, cancellationToken);
                        break;
                    case "/топ":
                        await HandleTopCommandAsync(message, cancellationToken);
                        break;
                    case "/ролл":
                        await HandleRollCommandAsync(message, cancellationToken);
                        break;
                }
            }
        }

        /// <summary>
        /// Обрабатывает команду /рост.
        /// </summary>
        /// <param name="message">Входящее сообщение.</param>
        /// <param name="cancellationToken">Токен для отмены выполнения.</param>
        /// <returns>Задача, представляющая асинхронную операцию.</returns>
        private async Task HandleHeightCommandAsync(VkNet.Model.Message message, CancellationToken cancellationToken)
        {
            var peerId = message.PeerId!.Value;
            var userId = message.FromId!.Value;

            using (var connection = new SQLiteConnection($"Data Source={_dbPath};Version=3;"))
            {
                connection.Open();

                // Проверяем, есть ли запись о пользователе
                var checkCommand = new SQLiteCommand(
                    "SELECT height, last_used FROM HeightData WHERE peer_id = @peerId AND user_id = @userId",
                    connection);
                checkCommand.Parameters.AddWithValue("@peerId", peerId);
                checkCommand.Parameters.AddWithValue("@userId", userId);

                using (var reader = checkCommand.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        // Запись существует, обновляем данные
                        var currentHeight = reader.GetInt32(0);
                        var lastUsed = DateTime.Parse(reader.GetString(1));
                        var timeSinceLastUsed = DateTime.UtcNow - lastUsed;

                        if (timeSinceLastUsed.TotalDays >= 1)
                        {
                            var heightIncrease = _random.Next(1, 11);
                            var newHeight = currentHeight + heightIncrease;

                            var updateCommand = new SQLiteCommand(
                                "UPDATE HeightData SET height = @height, last_used = @lastUsed " +
                                "WHERE peer_id = @peerId AND user_id = @userId",
                                connection);
                            updateCommand.Parameters.AddWithValue("@height", newHeight);
                            updateCommand.Parameters.AddWithValue("@lastUsed", DateTime.UtcNow.ToString("o")); // ISO 8601 format
                            updateCommand.Parameters.AddWithValue("@peerId", peerId);
                            updateCommand.Parameters.AddWithValue("@userId", userId);
                            updateCommand.ExecuteNonQuery();

                            await SendMessageAsync(peerId, $"Ваш рост увеличен на {heightIncrease} см! Текущий рост: {newHeight} см", cancellationToken);
                        }
                        else
                        {
                            var timeLeft = TimeSpan.FromDays(1) - timeSinceLastUsed;
                            await SendMessageAsync(peerId, $"Команду можно использовать только раз в сутки. Осталось ждать: {timeLeft.Hours} ч. {timeLeft.Minutes} мин.", cancellationToken);
                        }
                    }
                    else
                    {
                        // Записи нет, создаем новую
                        var initialHeight = _random.Next(1, 11);
                        var insertCommand = new SQLiteCommand(
                            "INSERT INTO HeightData (peer_id, user_id, height, last_used) " +
                            "VALUES (@peerId, @userId, @height, @lastUsed)",
                            connection);
                        insertCommand.Parameters.AddWithValue("@peerId", peerId);
                        insertCommand.Parameters.AddWithValue("@userId", userId);
                        insertCommand.Parameters.AddWithValue("@height", initialHeight);
                        insertCommand.Parameters.AddWithValue("@lastUsed", DateTime.UtcNow.ToString("o")); // ISO 8601 format
                        insertCommand.ExecuteNonQuery();

                        await SendMessageAsync(peerId, $"Ваш начальный рост равен {initialHeight} см!", cancellationToken);
                    }
                }
            }
        }

        /// <summary>
        /// Обрабатывает команду /топ.
        /// </summary>
        /// <param name="message">Входящее сообщение.</param>
        /// <param name="cancellationToken">Токен для отмены выполнения.</param>
        /// <returns>Задача, представляющая асинхронную операцию.</returns>
        private async Task HandleTopCommandAsync(VkNet.Model.Message message, CancellationToken cancellationToken)
        {
            var peerId = message.PeerId.Value;

            // Получаем список пользователей в беседе, чтобы сопоставить user_id с именами
            var conversationMembers = await _api.Messages.GetConversationMembersAsync(peerId,new List<string> { ProfileFields.FirstName.ToString(), ProfileFields.LastName.ToString() });

            var membersDict = conversationMembers.Profiles.ToDictionary(x => x.Id, x => $"{x.FirstName} {x.LastName}");

            List<(int Index, long UserId, int Height, string UserName)> sortedData;

            using (var connection = new SQLiteConnection($"Data Source={_dbPath};Version=3;"))
            {
                connection.Open();
                var command = new SQLiteCommand(
                    "SELECT user_id, height FROM HeightData WHERE peer_id = @peerId ORDER BY height DESC",
                    connection);
                command.Parameters.AddWithValue("@peerId", peerId);

                using (var reader = command.ExecuteReader())
                {
                    sortedData = new List<(int, long, int, string)>();
                    var index = 1;
                    while (reader.Read())
                    {
                        var userId = reader.GetInt64(0);
                        var height = reader.GetInt32(1);
                        var userName = membersDict.ContainsKey(userId) ? membersDict[userId] : "Неизвестный пользователь";
                        sortedData.Add((index++, userId, height, userName));
                    }
                }
            }

            if (sortedData.Count == 0)
            {
                await SendMessageAsync(peerId, "Нет данных для топа.", cancellationToken);
                return;
            }

            var topMessage = "Топ участников по росту:\n";
            foreach (var data in sortedData)
            {
                topMessage += $"{data.Index}. {data.UserName} - {data.Height} см\n";
            }

            await SendMessageAsync(peerId, topMessage, cancellationToken);
        }

        /// <summary>
        /// Обрабатывает команду /ролл.
        /// </summary>
        /// <param name="message">Входящее сообщение.</param>
        /// <param name="cancellationToken">Токен для отмены выполнения.</param>
        /// <returns>Задача, представляющая асинхронную операцию.</returns>
        private async Task HandleRollCommandAsync(VkNet.Model.Message message, CancellationToken cancellationToken)
        {
            var roll = _random.Next(1, 101);
            await SendMessageAsync(message.PeerId.Value, $"Выпало число: {roll}", cancellationToken);
        }

        /// <summary>
        /// Отправляет сообщение в чат.
        /// </summary>
        /// <param name="peerId">Идентификатор чата.</param>
        /// <param name="message">Текст сообщения.</param>
        /// <param name="cancellationToken">Токен для отмены выполнения.</param>
        /// <returns>Задача, представляющая асинхронную операцию.</returns>
        private async Task SendMessageAsync(long peerId, string message, CancellationToken cancellationToken)
        {
            await _api.Messages.SendAsync(new MessagesSendParams
            {
                PeerId = peerId,
                Message = message,
                RandomId = _random.Next()
            });
        }
    }

    /// <summary>
    /// Главный класс программы.
    /// </summary>
    internal class Program
    {
        /// <summary>
        /// Точка входа в программу.
        /// </summary>
        /// <param name="args">Аргументы командной строки.</param>
        public static void Main(string[] args)
        {
            // Вставьте сюда ваш токен доступа
            var accessToken = "YOUR_VK_API_TOKEN";

            var bot = new VkBot(accessToken);

            var cancellationTokenSource = new CancellationTokenSource();
            var botTask = bot.RunAsync(cancellationTokenSource.Token);

            Console.WriteLine("Press Enter to stop the bot.");
            Console.ReadLine();

            cancellationTokenSource.Cancel();
            botTask.Wait(); // Ожидаем завершения задачи бота

            Console.WriteLine("Bot stopped.");
        }
    }
}