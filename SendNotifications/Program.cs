using System;
using System.Collections.Generic;
using System.Data;
using MySql.Data.MySqlClient;
using System.Linq;
using System.Net.Mail;
using System.Net;

namespace SendNotifications
{
    public class Program
    {
        public static void Main()
        {
            Console.WriteLine("[{0}]: Send Notifications script starting...", DateTime.Now.ToString());

            int timeInterval = Convert.ToInt32(Environment.GetEnvironmentVariable("TIME_INTERVAL"));

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

            Console.WriteLine("[{0}]: Connecting to MySQL...", DateTime.Now.ToString());
            MySqlConnection connection = new MySqlConnection(connectionString);
            connection.Open();
            Console.WriteLine("[{0}]: Connection to MySQL successful!", DateTime.Now.ToString());

            List<string> matchdays = GetMatchdaysForNotifications(timeInterval, connection);

            if (!matchdays.Any())
            {
                return;
            }

            var fromAddress = new MailAddress(Environment.GetEnvironmentVariable("FROM_EMAIL"), Environment.GetEnvironmentVariable("FROM_NAME"));
            string password = Environment.GetEnvironmentVariable("EMAIL_PASSWORD");

            var smtp = new SmtpClient
            {
                Host = "smtp.gmail.com",
                Port = 587,
                EnableSsl = true,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(fromAddress.Address, password)
            };

            foreach (string matchday in matchdays)
            {
                List<Tuple<string, string>> users = GetNotifyEmailMatchdays(connection, matchday);
                string subject = GetSubject(timeInterval, matchday);

                foreach (var user in users)
                {
                    var toAddress = new MailAddress(user.Item2, user.Item1);
                    string body = GetBody(timeInterval, matchday, user.Item1);

                    using var message = new MailMessage(fromAddress, toAddress)
                    {
                        Subject = subject,
                        Body = body,
                        IsBodyHtml = true
                    };

                    smtp.Send(message);
                }
            }

            connection.Close();
        }

        private static string GetBody(int timeInterval, string matchday, string fullName)
        {
            string name = fullName.Split(' ')[0];

            if (timeInterval == 1)
            {
                return "Hi " + name + "!<br><br>Betting round for matchday " + matchday + " is closing in less than one hour!<br>Access www.penalty.duckdns.org and hit play to submit your prediction!<br><br>Best of luck!<br><img src=\"https://i.imgur.com/DPEiu1c.png\" style=\"width:224px;height:62px;\">";
            }
            else
            {
                return "Hi " + name + "!<br><br>Last call for predictions for matchday " + matchday + ". You have until the first match starts to submit your predictions!<br>Access www.penalty.duckdns.org and hit play to submit your prediction!<br><br>Best of luck!<br><img src=\"https://i.imgur.com/DPEiu1c.png\" style=\"width:224px;height:62px;\">";
            }
        }

        private static string GetSubject(int timeInterval, string matchday)
        {
            if (timeInterval == 1)
            {
                return "Sure you don't want to play? Last chance!";
            }
            else
            {
                return "Last call on bets for matchday " + matchday + "!";
            }
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

        private static List<string> GetMatchdaysForNotifications(int timeInterval, MySqlConnection connection)
        {
            MySqlCommand matchdaysForNotificationsCommand = new MySqlCommand("GetMatchdaysForNotifications", connection)
            {
                CommandType = CommandType.StoredProcedure
            };

            matchdaysForNotificationsCommand.Parameters.Add(new MySqlParameter("Hours", timeInterval));

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
