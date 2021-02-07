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

                logOutput.AppendLine(String.Format("[{0}]       [-] Getting today's matches", DateTime.Now.ToString()));
                string todaysMatches = TodaysMatches(connection);

                if (String.IsNullOrEmpty(todaysMatches))
                {
                    logOutput.AppendLine(String.Format("[{0}]       [-] No matches today", DateTime.Now.ToString()));
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

                logOutput.AppendLine(String.Format("[{0}]       [-] Sending notifications", DateTime.Now.ToString()));
                List<User> users = GetNotifyEmailMatchdays(connection);
                string subject = "Apostas do dia";

                foreach (var user in users)
                {
                    var toAddress = new MailAddress(user.Email, user.Name);
                    string body = GetBody(user);

                    using var message = new MailMessage(fromAddress, toAddress)
                    {
                        Subject = subject,
                        Body = body,
                        IsBodyHtml = true
                    };

                    logOutput.AppendLine(String.Format("[{0}]       [+] Sending notification to '" + user.Name, DateTime.Now.ToString()));
                    smtp.Send(message);
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

        private static string TodaysMatches(MySqlConnection connection)
        {
            MySqlCommand todaysMatchesCommand = new MySqlCommand("TodaysMatches", connection)
            {
                CommandType = CommandType.StoredProcedure
            };
            MySqlDataReader todaysMatchesReader = todaysMatchesCommand.ExecuteReader();

            string todaysMatches = null;
            if (todaysMatchesReader.HasRows)
            {
                todaysMatchesReader.Read();
                todaysMatches = Convert.ToString(todaysMatchesReader["TodaysMatches"]);
            }

            todaysMatchesReader.Close();

            return todaysMatches;
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

        private static string GetBody(User user)
        {
            string name = user.Name.Split(' ')[0];

            StringBuilder body = new StringBuilder();

            body.Append("Olá " + name);

            if (user.SendEmailReminder)
            {

            }

            if (user.SendEmailChange)
            {

            }

            return body.ToString();
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
                    SendEmailReminder = Convert.ToBoolean(notifyEmailMatchdaysReader["SendEmailReminder"]),
                    SendEmailChange = Convert.ToBoolean(notifyEmailMatchdaysReader["SendEmailChange"])
                });
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

        private class User
        {
            public string Username { get; set; }
            public string Name { get; set; }
            public string Email { get; set; }
            public bool SendEmailReminder { get; set; }
            public bool SendEmailChange { get; set; }
        }
    }
}
