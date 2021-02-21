using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using MySql.Data.MySqlClient;

namespace SendNotifications
{
    public class Program
    {
        public static string EmailHeader = File.ReadAllText("/app/EmailHeader");
        public static string EmailTable = File.ReadAllText("/app/EmailTable");
        public static string EmailFooter = File.ReadAllText("/app/EmailFooter");

        public static string BettingUrl;

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

                BettingUrl = Environment.GetEnvironmentVariable("BETTING_URL");

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

                logOutput.AppendLine(String.Format("[{0}]       [-] Opening update connection to MySQL", DateTime.Now.ToString()));
                MySqlConnection updateConnection = new MySqlConnection(connectionString);
                updateConnection.Open();
                logOutput.AppendLine(String.Format("[{0}]       [-] Update connection to MySQL successfully opened", DateTime.Now.ToString()));

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

                logOutput.AppendLine(String.Format("[{0}]       [-] Sending notifications", DateTime.Now.ToString()));
                List<User> users = GetNotifyEmailMatchdays(readConnection);
                string subject = "Apostas do dia";

                foreach (var user in users)
                {
                    var toAddress = new MailAddress(user.Email, user.Name);

                    MySqlCommand getTodaysBetsPerUserCommand;

                    if (user.Notifications == (int)NotificationType.MissingBets)
                    {
                        getTodaysBetsPerUserCommand = new MySqlCommand("GetTodaysMissingBetsPerUser", readConnection)
                        {
                            CommandType = CommandType.StoredProcedure
                        };
                    }
                    else if (user.Notifications == (int)NotificationType.AllBets)
                    {
                        getTodaysBetsPerUserCommand = new MySqlCommand("GetTodaysBetsPerUser", readConnection)
                        {
                            CommandType = CommandType.StoredProcedure
                        };
                    }
                    else
                    {
                        logOutput.AppendLine(String.Format("[{0}]       [!] Error processing notifications for '" + user.Username + "'", DateTime.Now.ToString()));
                        continue;
                    }

                    getTodaysBetsPerUserCommand.Parameters.Add(new MySqlParameter("User", user.Username));

                    logOutput.AppendLine(String.Format("[{0}]       [-] Getting matches for '" + user.Username + "'", DateTime.Now.ToString()));
                    MySqlDataReader getTodaysBetsPerUserReader = getTodaysBetsPerUserCommand.ExecuteReader();

                    if (!getTodaysBetsPerUserReader.HasRows)
                    {
                        logOutput.AppendLine(String.Format("[{0}]       [-] No notification needed for '" + user.Username + "'", DateTime.Now.ToString()));
                        continue;
                    }

                    logOutput.AppendLine(String.Format("[{0}]       [-] Generating email for '" + user.Username + "'", DateTime.Now.ToString()));
                    string body = GetBody(updateConnection, user, getTodaysBetsPerUserReader);

                    using var message = new MailMessage(fromAddress, toAddress)
                    {
                        Subject = subject,
                        Body = body,
                        IsBodyHtml = true
                    };

                    logOutput.AppendLine(String.Format("[{0}]       [+] Sending email to '" + user.Username + "'", DateTime.Now.ToString()));
                    smtp.Send(message);
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

        private static string GetBody(MySqlConnection connection, User user, MySqlDataReader matches)
        {
            string name = user.Name.Split(' ')[0];

            StringBuilder body = new StringBuilder();

            body.Append(EmailHeader);
            body.Append("Olá " + name + ",<br><br> Estas são as tuas apostas do dia:<br><br>");
            body.Append(EmailTable);

            while (matches.Read())
            {
                string tokenHome = GetToken();
                string tokenDraw = GetToken();
                string tokenAway = GetToken();

                string urlHome = BettingUrl + tokenHome;
                string urlDraw = BettingUrl + tokenDraw;
                string urlAway = BettingUrl + tokenAway;

                UpdateEmailBets(connection, matches.GetString("IdmatchAPI"), user.Username, tokenHome, tokenDraw, tokenAway);

                char result = matches.IsDBNull("Result") ? 'X' : matches.GetChar("Result");
                string styleHome = result.Equals('H') ? " style=\"background-color: black; color: white\">" : ">";
                string styleDraw = result.Equals('D') ? " style=\"background-color: black; color: white\">" : ">";
                string styleAway = result.Equals('A') ? " style=\"background-color: black; color: white\">" : ">";

                body.AppendLine("<tr style=\"box-sizing: border-box; page-break-inside: avoid;\">");
                body.AppendLine("<td>&nbsp;" + matches.GetString("Hometeam") + " </td>");
                body.AppendLine("<td" + styleHome + "&nbsp;<a href=\"" + urlHome + "\">" + matches.GetString("Oddshome") + "</a></td>");
                body.AppendLine("<td" + styleDraw + "&nbsp;<a href=\"" + urlDraw + "\">" + matches.GetString("Oddsdraw") + "</a></td>");
                body.AppendLine("<td" + styleAway + "&nbsp;<a href=\"" + urlAway + "\">" + matches.GetString("Oddsaway") + "</a></td>");
                body.AppendLine("<td>&nbsp;" + matches.GetString("Awayteam") + "</td>");
                body.AppendLine("<td> &nbsp; Data </td>");
                body.AppendLine("</tr>");
            }

            body.Append(EmailFooter);

            return body.ToString();
        }

        private static void UpdateEmailBets(MySqlConnection connection, string IdmatchAPI, string username, string tokenHome, string tokenDraw, string tokenAway)
        {
            MySqlCommand command = new MySqlCommand("UpdateEmailBets", connection)
            {
                CommandType = CommandType.StoredProcedure
            };

            command.Parameters.Add(new MySqlParameter("TokenHome", tokenHome));
            command.Parameters.Add(new MySqlParameter("TokenDraw", tokenDraw));
            command.Parameters.Add(new MySqlParameter("TokenAway", tokenAway));
            command.Parameters.Add(new MySqlParameter("Username", username));
            command.Parameters.Add(new MySqlParameter("IdmatchAPI", IdmatchAPI));

            command.ExecuteNonQuery();
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
                    Notifications = Convert.ToInt32(notifyEmailMatchdaysReader["Notifications"]),
                });
            }

            notifyEmailMatchdaysReader.Close();

            return users;
        }

        static string GetToken()
        {
            int i = 50;
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
            public int Notifications { get; set; }
        }

        enum NotificationType
        {
            NoNotifications,
            MissingBets,
            AllBets
        }
    }
}
