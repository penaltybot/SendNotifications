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
            Console.WriteLine("[{0}]: Update script starting...", DateTime.Now.ToString());

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

            var fromAddress = new MailAddress(Environment.GetEnvironmentVariable("FROM_EMAIL"), Environment.GetEnvironmentVariable("FROM_NAME"));
            string password = Environment.GetEnvironmentVariable("EMAIL_PASSWORD");
            string subject = "Subject";
            string body = "Body";

            var smtp = new SmtpClient
            {
                Host = "smtp.gmail.com",
                Port = 587,
                EnableSsl = true,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(fromAddress.Address, password)
            };

            Console.WriteLine("[{0}]: Connecting to MySQL...", DateTime.Now.ToString());
            MySqlConnection connection = new MySqlConnection(connectionString);
            connection.Open();
            Console.WriteLine("[{0}]: Connection to MySQL successful!", DateTime.Now.ToString());

            List<string> matchdays = GetMatchdaysForNotifications(timeInterval, connection);

            if (!matchdays.Any())
            {
                return;
            }

            foreach (string matchday in matchdays)
            {
                List<Tuple<string, string>> users = GetNotifyEmailMatchdays(connection, matchday);

                foreach (var user in users)
                {
                    var toAddress = new MailAddress(user.Item2, user.Item1);

                    using var message = new MailMessage(fromAddress, toAddress)
                    {
                        Subject = subject,
                        Body = body
                    };
                    smtp.Send(message);
                }
            }

            connection.Close();
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
                matchdays.Add(matchdaysForNotificationsReader.GetString("TeamId"));
            }

            matchdaysForNotificationsReader.Close();

            return matchdays;
        }
    }
}
