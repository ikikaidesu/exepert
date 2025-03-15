using Gameloop.Vdf;
using Gameloop.Vdf.Linq;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;


namespace LauncherTest
{
    // класс для определения .exe игры
    public class gameRating
    {
        public GameAndPath game;
        public string fullPath;
        public int rating;
    }
    // класс игры
    public class GameAndPath
    {
        public string name;
        public string path;
    }
    internal class Program
    {
        // запрещенные слова в .exe
        private static readonly string[] banWords = { "service", "web", "installer", "updater", "steamworks", "redistributable", "vc_redist", "server" };
        static void Main(string[] args)
        {
            // список названий игр(для метода который ищет через реестр)
            string[] games = { "Valorant", "Marvel Rivals", "PUBG: BATTLEGROUNDS", "Bread & Fred", "Deep Rock Galactic" };
            // пути реестра где игры(для метода который ищет через реестр)
            string[] paths = { "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall", 
                               "SOFTWARE\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall" };

            //checkGamesByReestr(games, paths);

            searchGamesBySteam();

            Console.WriteLine("Обход закончен");

            Console.ReadLine();
        }


        // поиск игр через реестр
        private static void checkGamesByReestr(string[] games, string[] startPaths)
        {
            // обходим пути внутри которых игры могут быть
            foreach (var path in startPaths)
            {
                // создаем базовый ключ(путь) реестра
                RegistryKey startKey = null;

                // для начала нужно определиться в какой системе будут программы(32/64)
                if (path.Contains("WOW6432Node"))
                {
                    // 32
                    startKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
                }
                else
                {
                    // 64
                    startKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                }

                // у стартового пути открываем путь из списка
                using (var subKey = startKey.OpenSubKey(path))
                {
                    // обходим все програмки внутри пути
                    foreach (var i in subKey.GetSubKeyNames())
                    {
                        // открываем данные програмки
                        using (var currentFile = subKey.OpenSubKey(i))
                        {
                            // получаем имя программы
                            var displayName = currentFile.GetValue("DisplayName");
                            // проверяем есть ли название вообще
                            if (displayName != null && String.IsNullOrEmpty(displayName.ToString()) != true)
                            {
                                // получаем это имя
                                string fileName = currentFile.GetValue("DisplayName").ToString();

                                // просматриваем есть ли этот файл в списке
                                if (games.Contains(fileName))
                                {
                                    string pathToGame = currentFile.GetValue("InstallLocation").ToString();
                                    Console.WriteLine($"Найдена игра - {fileName}\nПуть до игры - {pathToGame}\n");
                                }
                            }
                        }
                    }    
                }
            }
        }

        // метод поиска стим игр
        private static void searchGamesBySteam()
        {
            // путь в реестре к стиму
            const string steamRegistryPath = @"SOFTWARE\WOW6432Node\Valve\Steam";
            // путь от папки стима общей до файлика где хранится инфа о том, где хранятся другие папки стима с играми
            const string libraryFoldersFile = "/steamapps/libraryfolders.vdf";
            // хранилище для пути до стима
            string libraryFoldersPath = null;
            // список папок где хранятся игры чела
            List<string> gameLibraryPaths = new List<string>();
            // путь до папки содержащей инфу по скачанным программам(нужно чтобы прочитать файлы .acf)
            const string gamesManifestPath = "/steamapps/";
            // путь от начала папки где хранятся игры до входа в папку где сами игры
            const string commonGamesSubPath = "common/";

            // сначала через реестр получаем путь к установке стима
            // создаем ключ стартовый реестра 
            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
            {
                // сразу открываем путь дальше к стиму сразу
                using (RegistryKey steamKey = baseKey.OpenSubKey(steamRegistryPath))
                {
                    if (steamKey != null)
                    {
                        // получаем путь до стима 
                        var installPath = steamKey.GetValue("InstallPath");
                        // если пустой путь до стима
                        if (installPath != null && !string.IsNullOrEmpty(installPath.ToString()))
                        {
                            // при нахождении пути к стиму добавляем путь до файлика с данными по играм
                            libraryFoldersPath = installPath.ToString() + libraryFoldersFile;
                        }
                    }
                }
            }


            // проверяем существование файла
            if (File.Exists(libraryFoldersPath))
            {
                // читаем содержимое всего файла
                string configLines = File.ReadAllText(libraryFoldersPath);
                // превращаем содержимое файла обратно в vdf только в виде обьекта напоминающего словарик(есть ключ и значение)
                VObject steamVdf = VdfConvert.Deserialize(configLines).Value as VObject;

                // обходим ключи нашего объекта
                foreach (var entry in steamVdf)
                {
                    // у стима судя по моему файлу обьекты обозначаются числами от 0 до бесконечности,
                    // поэтому мы проверяем является ли ключ числом и если да берем из него путь и добавляем к нему доп. кусок пути к манифестам игр
                    // и сохраняем этот путь
                    if (int.TryParse(entry.Key, out _))
                    {
                        string path = entry.Value["path"].ToString().Replace("\\", "/") + gamesManifestPath;
                        gameLibraryPaths.Add(path);
                    }
                }
            }

            // создаем словарь куда будем сохранять название игры и название папки в которой она хранится в этой директории
            List<GameAndPath> gamesAndPaths = new List<GameAndPath>();
            // теперь читаем файлики манифесты 
            foreach (var path in gameLibraryPaths)
            {
                // получаем из текущей директории все файлы манифесты
                string[] manifests = Directory.GetFiles(path, "*.acf");

                // если такие файлы есть
                if (manifests.Length > 0)
                {
                    // обходим все манифесты которые нашли
                    foreach (var manifest in manifests)
                    {
                        // так как acf похож на vdf мы их можем одним способом читать
                        string configLines = File.ReadAllText(manifest);
                        // преобразуем в vdf объект
                        VObject manifestVdf = VdfConvert.Deserialize(configLines).Value as VObject;
                        // получаем нужные данные
                        string name = manifestVdf["name"].ToString();
                        string directory = manifestVdf["installdir"].ToString();

                        // создаем экземпляр и добавляем в список
                        GameAndPath game = new GameAndPath
                        {
                            name = name,
                            path = directory
                        };
                        gamesAndPaths.Add(game);
                    }
                }
            }

            // обходим пути еще раз наши и добавляем common чтобы перейти к играм
            for (int i  = 0; i < gameLibraryPaths.Count; i++)
            {
                gameLibraryPaths[i] += commonGamesSubPath;
            }

            // обходим пути к играм
            foreach (var path in gameLibraryPaths)
            {
                // получаем все папки с играми
                string[] gamePaths = gamesAndPaths.Select(ch => ch.path).ToArray();
                // получаем пути к папкам только тем что есть в манифестах(установленные)
                string[] directories = Directory.GetDirectories(path).Where(ch => gamePaths.Contains(ch.Split('/').Last())).ToArray();

                // проверяем есть ли там папки
                if (directories.Length > 0)
                {
                    // обходим их
                    foreach (var directory in directories)
                    {
                        // получаем из списка игр ту игру по которой обходим папку
                        var currentGame = gamesAndPaths.Where(ch => ch.path == directory.Split('/').Last()).First();
                        // передаем все данные в метод поиска игр
                        findGameExeInDirectory(directory, currentGame);
                    }
                }
            }
        }

        // метод поиска и вывода потенциального .exe игры
        private static void findGameExeInDirectory(string path, GameAndPath currentGame)
        {
            // получаем из папки с игрой все .exe файлы
            string[] executables = findAllExeFiles(path).ToArray();
            // проверяем что папка не пуста
            if (executables.Length > 0)
            {
                // создаем массив в котором всем полученным .exe присваиваем рейтинг, после чего убирая .exe c 0 рейтинга выводим тот у которого больше всего рейтинга
                gameRating[] games = executables
                    .Select(ch => new gameRating() 
                    { 
                        game = currentGame, 
                        fullPath = ch, 
                        rating = checkExeForGame(ch, currentGame)
                    })
                    .ToArray()
                    .Where(ch => ch.rating > 0)
                    .ToArray();

                if (games.Length > 0) 
                { 
                    Console.WriteLine($"игра - {games.OrderBy(ch => ch.rating).Last().fullPath}"); 
                }
            }
        }

        // метод нахождения всех .exe файлов внутри папки
        private static List<string> findAllExeFiles(string path)
        {
            List<string> executables = new List<string>();

            executables.AddRange(Directory.GetFiles(path, "*.exe"));

            string[] directories = Directory.GetDirectories(path);

            foreach (var i in directories)
            {
                executables.AddRange(findAllExeFiles(i));
            }

            return executables;
        }


        // метод определения потенциального .exe игры
        // он основан на рейтинге - чем больше совпадений мы находим у .exe тем больше вероятность что это тот что нам нужен
        private static int checkExeForGame(string path, GameAndPath game)
        {
            // получаем мета-данные .exe файла
            FileVersionInfo fileInfo = FileVersionInfo.GetVersionInfo(path);
            string gamePath = path.Split('/').Last().Split('\\').Last().ToLower();

            int rating = 0;

            // проверяем содержится ли название игры в мета-данных .exe 
            if (String.IsNullOrEmpty(fileInfo.ProductName) != true)
            {
                string productName = fileInfo.ProductName.ToLower();
                if (productName.Contains(game.name.ToLower()) || productName.Contains(game.path.ToLower()))
                {
                    rating += 1;
                }
            }
            // проверяем находится ли исходное название игры в названии .exe файла
            if (gamePath.Contains(game.name.ToLower()) || gamePath.Contains(game.path.ToLower()))
            {
                rating += 1;
            }
            // проверяем есть ли сокращение игры(counter-strike 2 = cs2) в названии .exe или если название такое же сокращенное то его очищаем и также проверяем
            if (gamePath.Contains(GetShortName(game.name)) || gamePath.Contains(GetShortName(getClearExeName(gamePath))))
            {
                rating += 1;
            }
            // проверка содержит ли .exe файл какую-то часть от названия игры
            if (checkExeNameForGameName(gamePath, game.name))
            {
                rating += 1;
            }

            // проверка на бан ворды в слове
            foreach (var word in banWords)
            {
                if (gamePath.Contains(word)) rating = 0;
            }

            return rating;
        }

        // метод получения сокращенного названия игры
        private static string GetShortName(string gameName)
        {

            // Удаляем все символы, кроме букв и цифр, заменяем их пробелами
            string cleanedName = Regex.Replace(gameName, @"[^a-zA-Z0-9]", " ");

            // Разбиваем на слова
            string[] words = cleanedName.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            // если название состоит из одного слова, то не нуждается в сокращении
            if (words.Length <= 1) return gameName;

            // тут мы получаем сокращенное название
            string shortName = "";
            foreach (var word in words)
            {
                if (char.IsLetter(word[0]))
                {
                    shortName += char.ToLower(word[0]);
                }
                else if (char.IsDigit(word[0])) 
                {
                    shortName += word;
                }
            }

            return shortName;
        }

        // метод получения чистого названия файла
        private static string getClearExeName(string name)
        {
            // Удаляем все символы, кроме букв и цифр, заменяем их пробелами
            string result =  Regex.Replace(name, @"[^a-zA-Z0-9]", "");
            return result;
        }

        // метод получения частей названия игры
        private static string[] getStringsFromGameName(string gameName)
        {
            // Удаляем все символы, кроме букв и цифр, заменяем их пробелами
            string cleanedName = Regex.Replace(gameName, @"[^a-zA-Z0-9]", " ");

            // Разбиваем на слова
            return cleanedName.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        // метод проверки есть ли часть названия игры в названии .exe файла
        private static bool checkExeNameForGameName(string exeName, string gameName)
        {
            foreach (var i in getStringsFromGameName(gameName))
            {
                if (exeName.ToLower().Contains(i.ToLower()))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
