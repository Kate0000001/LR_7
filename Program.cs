using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Dapper;     // NuGet: Dapper
using ScottPlot;  // NuGet: ScottPlot

namespace CombinedLabWork
{
    class Program
    {
        private static readonly HttpClient client = new HttpClient();

        private const string ServerName = @"(localdb)\MSSQLLocalDB";
        private const string DbName = "Cloth";

        private static string ConnectionString => $@"Server={ServerName};Database={DbName};Trusted_Connection=True;";

        private static string MasterConnectionString => $@"Server={ServerName};Database=master;Trusted_Connection=True;";

        static async Task Main(string[] args)
        {
            while (true)
            {
                Console.Clear();
                Console.WriteLine("=== ЛАБОРАТОРНАЯ РАБОТА: ОБЪЕДИНЕННОЕ РЕШЕНИЕ (ИСПРАВЛЕННОЕ) ===");
                Console.WriteLine("1. Задание №1 и №2: HTTP запросы, Лог и График");
                Console.WriteLine("2. Задание №3: База данных (Авто-создание + Dapper)");
                Console.WriteLine("3. Задание №4: Компилятор арифметических выражений");
                Console.WriteLine("4. Задание №5: Нагрузочное тестирование");
                Console.WriteLine("0. Выход");
                Console.WriteLine("================================================================");
                Console.Write("Выберите номер задания: ");

                string choice = Console.ReadLine();

                try
                {
                    switch (choice)
                    {
                        case "1": await RunTask1And2(); break;
                        case "2": await RunTask3(); break;
                        case "3": RunTask4(); break;
                        case "4": await RunTask5(); break;
                        case "0": return;
                        default: Console.WriteLine("Неверный выбор."); break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\n[!] Критическая ошибка: {ex.Message}");
                }

                Console.WriteLine("\nНажмите любую клавишу, чтобы вернуться в меню...");
                Console.ReadKey();
            }
        }

        #region --- Задание №1 и №2 ---
        static async Task RunTask1And2()
        {
            Console.WriteLine("\n--- Запуск Заданий 1 и 2 ---");
            string targetUrl = "https://www.saucedemo.com/";
            int requestCount = 100;
            List<string> urls = Enumerable.Repeat(targetUrl, requestCount).ToList();

            Stopwatch globalStopwatch = Stopwatch.StartNew();

            var tasks = urls.Select(async (url, index) =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    using var response = await client.GetAsync(url);
                    sw.Stop();
                    return sw.ElapsedMilliseconds;
                }
                catch
                {
                    return -1L;
                }
            });

            long[] results = await Task.WhenAll(tasks);
            globalStopwatch.Stop();

            var responseTimes = results.Where(t => t >= 0).ToList();
            double avgTime = responseTimes.Any() ? responseTimes.Average() : 0;

            Console.WriteLine($"Успешных запросов: {responseTimes.Count}/{requestCount}");
            Console.WriteLine($"Среднее время: {avgTime:F2} мс");

            await File.WriteAllLinesAsync("log.txt", responseTimes.Select(t => t.ToString()));
            Console.WriteLine("Лог сохранен: log.txt");

            if (responseTimes.Any())
            {
                ScottPlot.Plot plt = new();
                plt.Add.Scatter(
                    Enumerable.Range(1, responseTimes.Count).Select(x => (double)x).ToArray(),
                    responseTimes.Select(x => (double)x).ToArray()
                );
                plt.Title("Время отклика HTTP");
                plt.SavePng("response_time_plot.png", 600, 400);
                Console.WriteLine("График сохранен: response_time_plot.png");
            }
        }
        #endregion

        #region --- Задание №3: База данных ---
        static async Task RunTask3()
        {
            Console.WriteLine("\n--- Запуск Задания 3 (БД) ---");

            await EnsureDatabaseAndTableExist();

            try
            {
                using (var connection = new SqlConnection(ConnectionString))
                {
                    await connection.OpenAsync();


                    Console.WriteLine("Вставка 100 записей...");
                    var items = Enumerable.Range(1, 100).Select(i => new
                    {
                        Артикул = $"ART-{Guid.NewGuid().ToString().Substring(0, 6)}",
                        Наименование = $"Товар {i}",
                        Ширина = 10.0 + i,
                        Длина = 20.0 + i,
                        Изображение = "img.jpg",
                        Комментарий = "AutoTest"
                    });

                    string insertQuery = @"
                        INSERT INTO [dbo].[Изделие$] 
                        (Артикул, Наименование, Ширина, Длина, Изображение, Комментарий) 
                        VALUES (@Артикул, @Наименование, @Ширина, @Длина, @Изображение, @Комментарий)";

                    Stopwatch sw = Stopwatch.StartNew();
                    await connection.ExecuteAsync(insertQuery, items);
                    sw.Stop();
                    Console.WriteLine($"Вставка завершена за {sw.ElapsedMilliseconds} мс.");

                    // Чтение
                    Console.WriteLine("Чтение данных...");
                    sw.Restart();
                    var result = await connection.QueryAsync<dynamic>("SELECT * FROM [dbo].[Изделие$]");
                    sw.Stop();

                    Console.WriteLine($"Всего записей: {result.Count()}. Время чтения: {sw.ElapsedMilliseconds} мс.");
                    foreach (var item in result.TakeLast(3))
                    {
                        Console.WriteLine($" -> {item.Артикул}: {item.Наименование}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка выполнения SQL: {ex.Message}");
            }
        }
        static async Task EnsureDatabaseAndTableExist()
        {
            try
            {
                using (var masterConn = new SqlConnection(MasterConnectionString))
                {
                    await masterConn.OpenAsync();
                    var checkDb = await masterConn.QueryFirstOrDefaultAsync<int>(
                        $"SELECT 1 FROM sys.databases WHERE name = '{DbName}'");

                    if (checkDb == 0)
                    {
                        Console.WriteLine($"База данных '{DbName}' не найдена. Создаем...");
                        await masterConn.ExecuteAsync($"CREATE DATABASE [{DbName}]");
                        Console.WriteLine("База данных создана.");
                        await Task.Delay(1000);
                    }
                }

                using (var conn = new SqlConnection(ConnectionString))
                {
                    await conn.OpenAsync();
                    string checkTableQuery =
                        "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Изделие$'";

                    int tableExists = await conn.ExecuteScalarAsync<int>(checkTableQuery);

                    if (tableExists == 0)
                    {
                        Console.WriteLine("Таблица [Изделие$] не найдена. Создаем...");
                        string createTableSql = @"
                            CREATE TABLE [dbo].[Изделие$] (
                                [Id] INT IDENTITY(1,1) PRIMARY KEY,
                                [Артикул] NVARCHAR(50),
                                [Наименование] NVARCHAR(100),
                                [Ширина] FLOAT,
                                [Длина] FLOAT,
                                [Изображение] NVARCHAR(255),
                                [Комментарий] NVARCHAR(MAX)
                            )";
                        await conn.ExecuteAsync(createTableSql);
                        Console.WriteLine("Таблица создана.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка инициализации БД: {ex.Message}");
                Console.WriteLine("Убедитесь, что LocalDB установлен (компонент Visual Studio 'Data storage and processing').");
            }
        }
        #endregion

        #region --- Задание №4: Компилятор ---
        static void RunTask4()
        {
            Console.WriteLine("\n--- Запуск Задания 4 ---");
            string input = "2+(-5)*(7-8)";
            Console.WriteLine($"Пример выражения: {input}");
            try
            {
                double res = MathCompiler.Evaluate(input);
                Console.WriteLine($"Результат: {res}");
            }
            catch (Exception ex) { Console.WriteLine($"Ошибка: {ex.Message}"); }
        }

        public static class MathCompiler
        {
            public static double Evaluate(string expr) => CalculateRPN(ToRPN(expr));

            private static Queue<object> ToRPN(string expr)
            {
                var queue = new Queue<object>();
                var stack = new Stack<char>();
                for (int i = 0; i < expr.Length; i++)
                {
                    char c = expr[i];
                    if (char.IsDigit(c))
                    {
                        string s = c.ToString();
                        while (i + 1 < expr.Length && (char.IsDigit(expr[i + 1]) || expr[i + 1] == '.')) s += expr[++i];
                        queue.Enqueue(double.Parse(s, CultureInfo.InvariantCulture));
                    }
                    else if (c == '(') stack.Push(c);
                    else if (c == ')')
                    {
                        while (stack.Peek() != '(') queue.Enqueue(stack.Pop());
                        stack.Pop();
                    }
                    else if ("+-*/".Contains(c))
                    {
                        if (c == '-' && (i == 0 || expr[i - 1] == '(')) queue.Enqueue(0.0);
                        while (stack.Count > 0 && "+-*/".Contains(stack.Peek()) && Priority(stack.Peek()) >= Priority(c))
                            queue.Enqueue(stack.Pop());
                        stack.Push(c);
                    }
                }
                while (stack.Count > 0) queue.Enqueue(stack.Pop());
                return queue;
            }
            private static double CalculateRPN(Queue<object> queue)
            {
                var stack = new Stack<double>();
                foreach (var t in queue)
                    if (t is double d) stack.Push(d);
                    else
                    {
                        double b = stack.Pop(), a = stack.Pop();
                        stack.Push(((char)t) switch { '+' => a + b, '-' => a - b, '*' => a * b, '/' => a / b, _ => 0 });
                    }
                return stack.Pop();
            }
            private static int Priority(char c) => (c == '*' || c == '/') ? 2 : 1;
        }
        #endregion

        #region --- Задание №5: Load Test ---
        static async Task RunTask5()
        {
            Console.WriteLine("\n--- Запуск Задания 5 ---");
            string url = "https://www.saucedemo.com/";
            var threadsList = new List<double>();
            var timesList = new List<double>();

            for (int threads = 1; threads <= 64; threads *= 2)
            {
                var sw = Stopwatch.StartNew();
                await Task.WhenAll(Enumerable.Range(0, threads).Select(_ => client.GetAsync(url)));
                sw.Stop();

                double avg = sw.ElapsedMilliseconds / (double)threads;
                threadsList.Add(threads);
                timesList.Add(avg);
                Console.WriteLine($"Потоков: {threads}, Ср.время: {avg:F1} мс");
                await Task.Delay(300);
            }

            ScottPlot.Plot plt = new();
            var sp = plt.Add.Scatter(threadsList.ToArray(), timesList.ToArray());
            plt.SavePng("load_test.png", 600, 400);
            Console.WriteLine("График сохранен: load_test.png");
        }
        #endregion
    }
}
