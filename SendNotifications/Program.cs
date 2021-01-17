using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using MySql.Data.MySqlClient;

namespace SendNotifications
{
    public class Program
    {
        public static void Main()
        {
            Directory.CreateDirectory(@"/logs");

            StringBuilder logOutput = new StringBuilder();

            try
            {
                logOutput.AppendLine(String.Format("[{0}]       [-] Send Notifications script starting", DateTime.Now.ToString()));

                int lowerBound = Convert.ToInt32(Environment.GetEnvironmentVariable("LOWER_BOUND"));
                int upperBound = Convert.ToInt32(Environment.GetEnvironmentVariable("UPPER_BOUND"));

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

                logOutput.AppendLine(String.Format("[{0}]       [-] Connecting to MySQL", DateTime.Now.ToString()));
                MySqlConnection connection = new MySqlConnection(connectionString);
                connection.Open();
                logOutput.AppendLine(String.Format("[{0}]       [-] Connection to MySQL successful", DateTime.Now.ToString()));

                logOutput.AppendLine(String.Format("[{0}]       [-] Getting matchdays for notifications", DateTime.Now.ToString()));
                List<string> matchdays = GetMatchdaysForNotifications(lowerBound, upperBound, connection);

                if (!matchdays.Any())
                {
                    logOutput.AppendLine(String.Format("[{0}]       [-] No notifications to be sent", DateTime.Now.ToString()));
                    logOutput.AppendLine(String.Format("[{0}]       [-] Job finished", DateTime.Now.ToString()));
                    ProcessLogs(logOutput.ToString());

                    return;
                }

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

                string emailType = Environment.GetEnvironmentVariable("EMAIL_TYPE");

                logOutput.AppendLine(String.Format("[{0}]       [-] Sending notifications", DateTime.Now.ToString()));
                foreach (string matchday in matchdays)
                {
                    List<Tuple<string, string>> users = GetNotifyEmailMatchdays(connection, matchday);
                    string subject = GetSubject(emailType, matchday);

                    foreach (var user in users)
                    {
                        var toAddress = new MailAddress(user.Item2, user.Item1);
                        string body = GetBody(emailType, matchday, user.Item1);

                        using var message = new MailMessage(fromAddress, toAddress)
                        {
                            Subject = subject,
                            Body = body,
                            IsBodyHtml = true
                        };

                        logOutput.AppendLine(String.Format("[{0}]       [+] Sending notification to '" + user.Item1 + "' for matchday '" + matchday + "'", DateTime.Now.ToString()));
                        smtp.Send(message);
                    }
                }

                logOutput.AppendLine(String.Format("[{0}]       [-] Closing connection to MySQL", DateTime.Now.ToString()));
                connection.Close();
                logOutput.AppendLine(String.Format("[{0}]       [-] Connection to MySQL successful closed", DateTime.Now.ToString()));
                logOutput.AppendLine(String.Format("[{0}]       [-] Job finished", DateTime.Now.ToString()));
            }
            catch (Exception ex)
            {
                logOutput.AppendLine(String.Format("[{0}]       [!] Job failed with exception:", DateTime.Now.ToString()));
                logOutput.AppendLine(ex.ToString());
            }

            ProcessLogs(logOutput.ToString());
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

        private static string GetBody(string emailType, string matchday, string fullName)
        {
            string name = fullName.Split(' ')[0];

            return emailType switch
            {
                "DAILY" => "Hi " + name + "!<br><br>Last call for predictions for matchday " + matchday + ". You have until the first match starts to submit your predictions!<br>Access www.penalty.duckdns.org and hit play to submit your prediction!<br><br>Best of luck!<br><img src=\"https://i.imgur.com/DPEiu1c.png\" style=\"width:224px;height:62px;\">",
                "HOURLY" => "Hi " + name + "!<br><br>Betting round for matchday " + matchday + " is closing in just a couple of hours!<br>Access www.penalty.duckdns.org and hit play to submit your prediction!<br><br>Best of luck!<br><img src=\"https://i.imgur.com/DPEiu1c.png\" style=\"width:224px;height:62px;\">",
                _ => null,
            };
        }

        private static string GetSubject(string emailType, string matchday)
        {
            return emailType switch
            {
                "DAILY" => "Last call on bets for matchday " + matchday + "!",
                "HOURLY" => "Sure you don't want to play? Last chance!",
                _ => null,
            };
        }

        private static List<Tuple<string, string>> GetNotifyEmailMatchdays(MySqlConnection connection, string matchday)
        {
            MySqlCommand notifyEmailMatchdaysCommand = new MySqlCommand("GetNotifyEmailMatchdays", connection)
            {
                CommandType = CommandType.StoredProcedure
            };

            notifyEmailMatchdaysCommand.Parameters.Add(new MySqlParameter("Matchday", matchday));

            MySqlDataReader notifyEmailMatchdaysReader = notifyEmailMatchdaysCommand.ExecuteReader();

            List<Tuple<string, string>> users = new List<Tuple<string, string>>();
            while (notifyEmailMatchdaysReader.Read())
            {
                users.Add(new Tuple<string, string>(notifyEmailMatchdaysReader.GetString("Name"), notifyEmailMatchdaysReader.GetString("Email")));
            }

            notifyEmailMatchdaysReader.Close();

            return users;
        }

        private static List<string> GetMatchdaysForNotifications(int lowerBound, int upperBound, MySqlConnection connection)
        {
            MySqlCommand matchdaysForNotificationsCommand = new MySqlCommand("GetMatchdaysForNotifications", connection)
            {
                CommandType = CommandType.StoredProcedure
            };

            matchdaysForNotificationsCommand.Parameters.Add(new MySqlParameter("LowerBound", lowerBound));
            matchdaysForNotificationsCommand.Parameters.Add(new MySqlParameter("UpperBound", upperBound));

            MySqlDataReader matchdaysForNotificationsReader = matchdaysForNotificationsCommand.ExecuteReader();

            List<string> matchdays = new List<string>();
            while (matchdaysForNotificationsReader.Read())
            {
                matchdays.Add(matchdaysForNotificationsReader.GetString("Matchday"));
            }

            matchdaysForNotificationsReader.Close();

            return matchdays;
        }
    }
}
