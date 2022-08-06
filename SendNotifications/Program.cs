using MySql.Data.MySqlClient;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace SendNotifications
{
    public class Program
    {
        public static string EmailHeader = File.ReadAllText("/app/EmailHeader");
        public static string EmailTable = File.ReadAllText("/app/EmailTable");
        public static string EmailFooter = File.ReadAllText("/app/EmailFooter");

        public static string TelegramHeader = File.ReadAllText("/app/TelegramHeader");
        public static string TelegramFrame = File.ReadAllText("/app/TelegramFrame");
        public static string TelegramFooter = File.ReadAllText("/app/TelegramFooter");

        public static string WebSiteLocation;

        public static void Main()
        {
            Directory.CreateDirectory(@"/logs");

            StringBuilder logOutput = new StringBuilder();

            try
            {
                logOutput.AppendLine(String.Format("[{0}]       [-] Send Notifications script starting", DateTime.Now.ToString()));

                logOutput.AppendLine(String.Format("[{0}]       [-] Retrieving necessary environment variables", DateTime.Now.ToString()));
                string connectionString = string.Format(
                    "server={0};user={1};password={2};port={3};database={4}",
                    new string[]
                    {
                    Environment.GetEnvironmentVariable("DB_URL"),
                    Environment.GetEnvironmentVariable("DB_USER"),
                    Environment.GetEnvironmentVariable("DB_PASSWORD"),
                    Environment.GetEnvironmentVariable("DB_PORT"),
                    Environment.GetEnvironmentVariable("DB_DATABASE")
                    });

                logOutput.AppendLine(String.Format("[{0}]       [-] Opening read connection to MySQL", DateTime.Now.ToString()));
                MySqlConnection readConnection = new MySqlConnection(connectionString);
                readConnection.Open();
                logOutput.AppendLine(String.Format("[{0}]       [-] Read connection to MySQL successfully opened", DateTime.Now.ToString()));

                logOutput.AppendLine(String.Format("[{0}]       [+] Truncating old tokens", DateTime.Now.ToString()));
                MySqlCommand truncateEmailBetsCommand = new MySqlCommand("TruncateEmailBets", readConnection)
                {
                    CommandType = CommandType.StoredProcedure
                };
                truncateEmailBetsCommand.ExecuteNonQuery();

                logOutput.AppendLine(String.Format("[{0}]       [-] Getting today's matches", DateTime.Now.ToString()));
                if (!MatchesToday(readConnection))
                {
                    logOutput.AppendLine(String.Format("[{0}]       [-] No matches today", DateTime.Now.ToString()));
                    logOutput.AppendLine(String.Format("[{0}]       [-] Job finished", DateTime.Now.ToString()));
                    ProcessLogs(logOutput.ToString());

                    return;
                }

                logOutput.AppendLine(String.Format("[{0}]       [-] Getting global constants", DateTime.Now.ToString()));
                Dictionary<string, string> globalConstants = GetGlobalConstants(readConnection);
                var apiUrlTelegram = globalConstants["API_URL_TELEGRAM"];
                var telegramBotToken = globalConstants["TOKEN_BOT_TELEGRAM"];
                var telegramChatId = globalConstants["CHAT_ID_TELEGRAM"];
                var reportUrl = globalConstants["WEBSITE_LOCATION"];
                WebSiteLocation = globalConstants["WEBSITE_LOCATION"];


                logOutput.AppendLine(String.Format("[{0}]       [-] Opening additional connections to MySQL", DateTime.Now.ToString()));
                MySqlConnection updateConnection = new MySqlConnection(connectionString);
                updateConnection.Open();
                MySqlConnection betsPerUserConnection = new MySqlConnection(connectionString);
                betsPerUserConnection.Open();
                MySqlConnection missingBetsPerUserConnection = new MySqlConnection(connectionString);
                missingBetsPerUserConnection.Open();
                logOutput.AppendLine(String.Format("[{0}]       [-] Additional connections to MySQL successfully opened", DateTime.Now.ToString()));

                logOutput.AppendLine(String.Format("[{0}]       [-] Establishing SMTP session", DateTime.Now.ToString()));
                var fromAddress = new MailAddress(Environment.GetEnvironmentVariable("FROM_EMAIL"), Environment.GetEnvironmentVariable("FROM_NAME"));
                string emailPassword = Environment.GetEnvironmentVariable("EMAIL_PASSWORD");

                var smtp = new SmtpClient
                {
                    Host = "smtp.gmail.com",
                    Port = 587,
                    EnableSsl = true,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(fromAddress.Address, emailPassword)
                };
                logOutput.AppendLine(String.Format("[{0}]       [-] SMTP session established successfully", DateTime.Now.ToString()));

                logOutput.AppendLine(String.Format("[{0}]       [-] Getting users", DateTime.Now.ToString()));
                List<User> users = GetNotifyEmailMatchdays(readConnection);

                logOutput.AppendLine(String.Format("[{0}]       [-] Closing read connection to MySQL", DateTime.Now.ToString()));
                logOutput.AppendLine(String.Format("[{0}]       [-] Read connection to MySQL successful closed", DateTime.Now.ToString()));

                string subject = "Apostas do dia";

                foreach (var user in users)
                {
                    var toAddress = new MailAddress(user.Email, user.Name);
                    logOutput.AppendLine(String.Format("[{0}]       [-] Getting matches", DateTime.Now.ToString()));
                    var matches = GetMatches(readConnection, user.Username, logOutput);
                    var missingMatches = matches.Where(match => match.Result == null).ToList();
                    UpdateEmailBets(updateConnection, user.Username, matches);

                    if ((user.EmailNotifications == (int)NotificationType.AllBets) || (user.EmailNotifications == (int)NotificationType.MissingBets && HasMissingBets(matches)))
                    {
                        logOutput.AppendLine(String.Format("[{0}]       [-] Generating email body for '" + user.Username + "'", DateTime.Now.ToString()));
                        string emailBody;

                        if (user.EmailNotifications == (int)NotificationType.AllBets)
                        {
                            emailBody = GetEmailBody(user, matches);
                        }
                        else
                        {
                            emailBody = GetEmailBody(user, missingMatches);
                        }

                        using var message = new MailMessage(fromAddress, toAddress)
                        {
                            Subject = subject,
                            Body = emailBody,
                            IsBodyHtml = true
                        };

                        logOutput.AppendLine(String.Format("[{0}]       [+] Sending email to '" + user.Username + "'", DateTime.Now.ToString()));
                        smtp.Send(message);
                    }
                    else
                    {
                        logOutput.AppendLine(String.Format("[{0}]       [-] No email notification needed for '" + user.Username + "'", DateTime.Now.ToString()));
                    }


                    if ((user.TelegramNotifications == (int)NotificationType.AllBets) || (user.TelegramNotifications == (int)NotificationType.MissingBets && HasMissingBets(matches)))
                    {
                        logOutput.AppendLine(String.Format("[{0}]       [-] Generating telegram body for '" + user.Username + "'", DateTime.Now.ToString()));
                        string telegramBody;

                        if (user.TelegramNotifications == (int)NotificationType.AllBets)
                        {
                            telegramBody = GetTelegramBody(user, matches);
                        }
                        else
                        {
                            telegramBody = GetTelegramBody(user, missingMatches);
                        }

                        logOutput.AppendLine(String.Format("[{0}]       [+] Sending telegram notification to '" + user.Username + "'", DateTime.Now.ToString()));
                        SendTelegramMessage(apiUrlTelegram, telegramBotToken, user.Telegram, telegramBody);
                    }
                    else
                    {
                        logOutput.AppendLine(String.Format("[{0}]       [-] No telegram notification needed for '" + user.Username + "'", DateTime.Now.ToString()));
                    }
                }

                logOutput.AppendLine(String.Format("[{0}]       [-] Closing read connection to MySQL", DateTime.Now.ToString()));
                readConnection.Close();
                logOutput.AppendLine(String.Format("[{0}]       [-] Read connection to MySQL successful closed", DateTime.Now.ToString()));

                logOutput.AppendLine(String.Format("[{0}]       [-] Closing update connection to MySQL", DateTime.Now.ToString()));
                updateConnection.Close();
                logOutput.AppendLine(String.Format("[{0}]       [-] Update connection to MySQL successful closed", DateTime.Now.ToString()));

                logOutput.AppendLine(String.Format("[{0}]       [-] Job finished", DateTime.Now.ToString()));
            }
            catch (Exception ex)
            {
                logOutput.AppendLine(String.Format("[{0}]       [!] Job failed with exception:", DateTime.Now.ToString()));
                logOutput.AppendLine(ex.ToString());
            }

            ProcessLogs(logOutput.ToString());
        }

        private static string GetTelegramBody(User user, List<Match> matches)
        {
            string name = user.Name.Split(' ')[0];
            StringBuilder body = new StringBuilder();

            body.Append(TelegramHeader);
            body.Append(name);
            body.Append(TelegramFrame);

            foreach (var match in matches)
            {
                string urlHome = WebSiteLocation + "/Home/SubmitEmailBet?token=" + match.TokenHome;
                string urlDraw = WebSiteLocation + "/Home/SubmitEmailBet?token=" + match.TokenDraw;
                string urlAway = WebSiteLocation + "/Home/SubmitEmailBet?token=" + match.TokenAway;

                char result = match.Result == null ? 'X' : Convert.ToChar(match.Result);
                string styleHome = result.Equals('H') ? "[<a href=\"" + urlHome + "\">" + match.OddsHome + "</a>]" : "<a href=\"" + urlHome + "\">" + match.OddsHome + "</a>";
                string styleDraw = result.Equals('D') ? "[<a href=\"" + urlDraw + "\">" + match.OddsDraw + "</a>]" : "<a href=\"" + urlDraw + "\">" + match.OddsDraw + "</a>";
                string styleAway = result.Equals('A') ? "[<a href=\"" + urlAway + "\">" + match.OddsAway + "</a>]" : "<a href=\"" + urlAway + "\">" + match.OddsAway + "</a>";
                string fullOdds = "|  " + styleHome + "  |  " + styleDraw + "  |  " + styleAway + "  |";

                body.Append("\n\n<b>" + match.Hometeam + "</b> - <b>" + match.Awayteam + "</b> : " + match.UtcDate + "\n");
                body.Append(fullOdds);
            }

            body.Append("\n\n");
            body.Append(TelegramFooter);
            body.Append("\n\n");

            return body.ToString();
        }

        private static bool HasMissingBets(List<Match> matches)
        {
            return matches.Count(match => match.Result == null) > 0;
        }

        private static List<Match> GetMatches(MySqlConnection connection, string username, StringBuilder logOutput)
        {
            MySqlCommand getNotificationMatchesCommand = new MySqlCommand("GetNotificationMatches", connection)
            {
                CommandType = CommandType.StoredProcedure
            };
            getNotificationMatchesCommand.Parameters.Add(new MySqlParameter("User", username));

            MySqlDataReader getNotificationMatchesReader = getNotificationMatchesCommand.ExecuteReader();

            List<Match> matches = new List<Match>();
            while (getNotificationMatchesReader.Read())
            {
                if (getNotificationMatchesReader.IsDBNull("Oddshome"))
                {
                    logOutput.AppendLine(String.Format("[{0}]       [!] No odds for 'Home' for FixtureId='{1}'; Skipping...", DateTime.Now.ToString(), getNotificationMatchesReader.GetString("IdmatchAPI")));
                    continue;
                }
                if (getNotificationMatchesReader.IsDBNull("Oddsdraw"))
                {
                    logOutput.AppendLine(String.Format("[{0}]       [!] No odds for 'Draw' for FixtureId='{1}'; Skipping...", DateTime.Now.ToString(), getNotificationMatchesReader.GetString("IdmatchAPI")));
                    continue;
                }
                if (getNotificationMatchesReader.IsDBNull("Oddsaway"))
                {
                    logOutput.AppendLine(String.Format("[{0}]       [!] No odds for 'Away' for FixtureId='{1}'; Skipping...", DateTime.Now.ToString(), getNotificationMatchesReader.GetString("IdmatchAPI")));
                    continue;
                }

                char? result = '-';
                if (!getNotificationMatchesReader.IsDBNull("Result"))
                {
                    result = getNotificationMatchesReader.GetChar("Result");
                }

                matches.Add(new Match()
                {
                    IdmatchAPI = getNotificationMatchesReader.GetString("IdmatchAPI"),
                    Hometeam = getNotificationMatchesReader.GetString("Hometeam"),
                    Awayteam = getNotificationMatchesReader.GetString("Awayteam"),
                    OddsHome = getNotificationMatchesReader.GetString("Oddshome"),
                    OddsDraw = getNotificationMatchesReader.GetString("Oddsdraw"),
                    OddsAway = getNotificationMatchesReader.GetString("Oddsaway"),
                    TokenHome = GetToken(),
                    TokenDraw = GetToken(),
                    TokenAway = GetToken(),
                    UtcDate = getNotificationMatchesReader.GetString("UtcDate"),
                    Result = result
                });
            }

            getNotificationMatchesReader.Close();

            return matches;
        }

        private static bool MatchesToday(MySqlConnection connection)
        {
            MySqlCommand todaysMatchesCommand = new MySqlCommand("TodaysMatches", connection)
            {
                CommandType = CommandType.StoredProcedure
            };
            MySqlDataReader todaysMatchesReader = todaysMatchesCommand.ExecuteReader();

            if (todaysMatchesReader.HasRows)
            {
                todaysMatchesReader.Close();
                return true;
            }

            todaysMatchesReader.Close();
            return false;
        }

        private static void ProcessLogs(string logOutput)
        {
            string logOutputPath = "/logs/log_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log";

            File.AppendAllText(logOutputPath, logOutput);

            if (logOutput.Contains("[!]"))
            {
                var fromAddress = Environment.GetEnvironmentVariable("FROM_EMAIL");
                string operatorEmails = Environment.GetEnvironmentVariable("OPERATOR_EMAILS");
                string emailPassword = Environment.GetEnvironmentVariable("EMAIL_PASSWORD");

                var smtp = new SmtpClient
                {
                    Host = "smtp.gmail.com",
                    Port = 587,
                    EnableSsl = true,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(fromAddress, emailPassword)
                };

                using var message = new MailMessage(fromAddress, operatorEmails)
                {
                    Subject = "Errors in job!",
                    Body = "Hi operators,<br><br>An error has occured in today's scheduled job!<br>Log file has been attached to this message.",
                    IsBodyHtml = true,
                };

                message.Attachments.Add(new Attachment(logOutputPath));

                smtp.Send(message);
            }

            string[] files = Directory.GetFiles("/logs");

            foreach (string file in files)
            {
                FileInfo fileInfo = new FileInfo(file);
                if (fileInfo.LastAccessTime < DateTime.Now.AddMonths(-1))
                {
                    fileInfo.Delete();
                }
            }
        }

        private static string GetEmailBody(User user, List<Match> matches)
        {
            string name = user.Name.Split(' ')[0];

            StringBuilder body = new StringBuilder();

            body.Append(EmailHeader);
            body.Append(name);
            body.Append(EmailTable);

            foreach (var match in matches)
            {
                string urlHome = WebSiteLocation + "/Home/SubmitEmailBet?token=" + match.TokenHome;
                string urlDraw = WebSiteLocation + "/Home/SubmitEmailBet?token=" + match.TokenDraw;
                string urlAway = WebSiteLocation + "/Home/SubmitEmailBet?token=" + match.TokenAway;

                char result = match.Result == null ? 'X' : Convert.ToChar(match.Result);
                string styleHome = result.Equals('H') ? " style=\"background-color: black; color: white\">" : ">";
                string styleDraw = result.Equals('D') ? " style=\"background-color: black; color: white\">" : ">";
                string styleAway = result.Equals('A') ? " style=\"background-color: black; color: white\">" : ">";

                body.Append("<tr style=\"box-sizing: border-box; page-break-inside: avoid;\">");
                body.Append("<td>&nbsp;" + match.Hometeam + " </td>");
                body.Append("<td" + styleHome + "&nbsp;<a href=\"" + urlHome + "\">" + match.OddsHome + "</a></td>");
                body.Append("<td" + styleDraw + "&nbsp;<a href=\"" + urlDraw + "\">" + match.OddsDraw + "</a></td>");
                body.Append("<td" + styleAway + "&nbsp;<a href=\"" + urlAway + "\">" + match.OddsAway + "</a></td>");
                body.Append("<td>&nbsp;" + match.Awayteam + "</td>");
                body.Append("<td>&nbsp;" + match.UtcDate + "</td>");
                body.Append("</tr>");
            }

            body.Append(EmailFooter);

            return body.ToString();
        }

        private static void SendTelegramMessage(string apiUrlTelegram, string telegramBotToken, string telegramChatId, string body)
        {
            var apiTelegramUrl = new RestClient(apiUrlTelegram + telegramBotToken + "/sendMessage");

            var apiTelegramRequest = new RestRequest();
            apiTelegramRequest.AddQueryParameter("chat_id", telegramChatId);
            apiTelegramRequest.AddQueryParameter("text", body);
            apiTelegramRequest.AddQueryParameter("parse_mode", "HTML");

            Thread.Sleep(5000);
            apiTelegramUrl.Execute(apiTelegramRequest);
        }

        private static void UpdateEmailBets(MySqlConnection connection, string username, List<Match> matches)
        {
            foreach (var match in matches)
            {
                MySqlCommand command = new MySqlCommand("UpdateEmailBets", connection)
                {
                    CommandType = CommandType.StoredProcedure
                };

                command.Parameters.Add(new MySqlParameter("IdmatchAPI", match.IdmatchAPI));
                command.Parameters.Add(new MySqlParameter("Username", username));
                command.Parameters.Add(new MySqlParameter("TokenHome", match.TokenHome));
                command.Parameters.Add(new MySqlParameter("TokenDraw", match.TokenDraw));
                command.Parameters.Add(new MySqlParameter("TokenAway", match.TokenAway));

                command.ExecuteNonQuery();
            }
        }

        private static Dictionary<string, string> GetGlobalConstants(MySqlConnection connection)
        {
            MySqlCommand globalConstantsCommand = new MySqlCommand("GetGlobalConstants", connection)
            {
                CommandType = CommandType.StoredProcedure
            };
            MySqlDataReader globalConstantsReader = globalConstantsCommand.ExecuteReader();

            Dictionary<string, string> globalConstants = new Dictionary<string, string>();
            while (globalConstantsReader.Read())
            {
                globalConstants.Add(
                    globalConstantsReader["Constant"].ToString(),
                    globalConstantsReader["Value"].ToString());
            }

            globalConstantsReader.Close();

            return globalConstants;
        }


        private static List<User> GetNotifyEmailMatchdays(MySqlConnection connection)
        {
            MySqlCommand notifyEmailMatchdaysCommand = new MySqlCommand("GetNotifyEmailUsers", connection)
            {
                CommandType = CommandType.StoredProcedure
            };

            MySqlDataReader notifyEmailMatchdaysReader = notifyEmailMatchdaysCommand.ExecuteReader();

            List<User> users = new List<User>();
            while (notifyEmailMatchdaysReader.Read())
            {
                users.Add(new User()
                {
                    Username = notifyEmailMatchdaysReader.GetString("UserName"),
                    Name = notifyEmailMatchdaysReader.GetString("Name"),
                    Email = notifyEmailMatchdaysReader.GetString("Email"),
                    Telegram = notifyEmailMatchdaysReader.GetString("Telegram"),
                    EmailNotifications = Convert.ToInt32(notifyEmailMatchdaysReader["EmailNotifications"]),
                    TelegramNotifications = Convert.ToInt32(notifyEmailMatchdaysReader["TelegramNotifications"])
                });
            }

            notifyEmailMatchdaysReader.Close();

            return users;
        }

        static string GetToken()
        {
            int i = 100;
            const string valid = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890";
            StringBuilder res = new StringBuilder();
            using (RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider())
            {
                byte[] uintBuffer = new byte[sizeof(uint)];

                while (i-- > 0)
                {
                    rng.GetBytes(uintBuffer);
                    uint num = BitConverter.ToUInt32(uintBuffer, 0);
                    res.Append(valid[(int)(num % (uint)valid.Length)]);
                }
            }

            return res.ToString();
        }

        private class User
        {
            public string Username { get; set; }
            public string Name { get; set; }
            public string Email { get; set; }
            public string Telegram { get; set; }
            public int EmailNotifications { get; set; }
            public int TelegramNotifications { get; set; }
        }

        private class Match
        {
            public string IdmatchAPI { get; set; }
            public string Hometeam { get; set; }
            public string Awayteam { get; set; }
            public string OddsHome { get; set; }
            public string OddsDraw { get; set; }
            public string OddsAway { get; set; }
            public string TokenHome { get; set; }
            public string TokenDraw { get; set; }
            public string TokenAway { get; set; }

            public string UtcDate { get; set; }
            public char? Result { get; set; }
        }

        enum NotificationType
        {
            NoNotifications,
            MissingBets,
            AllBets
        }

        public static StringBuilder ReplaceSubstring(StringBuilder stringBuilder, int index, string replacement)
        {
            if (index + replacement.Length > stringBuilder.Length)
            {
                throw new ArgumentOutOfRangeException();
            }

            for (int i = 0; i < replacement.Length; ++i)
            {
                stringBuilder[index + i] = replacement[i];
            }

            return stringBuilder;
        }
    }
}