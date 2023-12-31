﻿using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using smsBackendGateway.Models;
using System.IO.Ports;
using System.Text.RegularExpressions;
using System.Text.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using NuGet.Protocol.Plugins;
using Microsoft.DotNet.Scaffolding.Shared.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.VisualBasic;
using Microsoft.EntityFrameworkCore.Metadata.Internal;



namespace smsBackendGateway.Controllers
{
    [ApiController]
    [Route("[controller]")]

    public class SmsController : ControllerBase
    {
        private SerialPort serialport = new SerialPort("COM4", 115200);
        private string[] ports = SerialPort.GetPortNames();

        private readonly ILogger<SmsController> _logger;
        private readonly SmsContext _dbContext;
        
        public SmsController(ILogger<SmsController> logger, SmsContext DbContext)
        {
            _logger = logger;
            _dbContext = DbContext;
            this.serialport.Parity = Parity.None;
            this.serialport.DataBits = 8;
            this.serialport.StopBits = StopBits.One;
            this.serialport.Handshake = Handshake.RequestToSend;
            this.serialport.DtrEnable = true;
            this.serialport.RtsEnable = true;
            this.serialport.NewLine = System.Environment.NewLine;
        }

        private List<Sms> ParseSmsMessages(string response)
        {
            int ctr = 0;
            int messageId = -1;
            string? dateAndTime = null;
            string? message = null;
            string? sender = null;

            List<Sms> messages = new List<Sms>();

            // Split the response into lines.
            string[] lines = response.Split('\n');

            foreach (string line in lines)
            {
                if (line.StartsWith("+CMGL"))
                {
                    ctr = 0;

                    string[] index = line.Split(',');

                    messageId = int.Parse(index[0].Substring(7).ToString());

                    string status = index[1].Trim('\"');

                    sender = "+" + index[2].Trim('\"');

                    dateAndTime = (index[4] + ", " + index[5]).Trim('\"');

                    ctr++;
                }
                else
                {
                    if (ctr == 1)
                    {
                        message = line.ToString();
                        messages.Add(new Sms("1", message, messageId, sender, dateAndTime));
                        ctr = 0;
                    }
                }
            }
            return messages;
        }

        [HttpGet]
        [Route("ReceiveMessage")]
        public string GetAllMessages()
        {
            serialport.Open();

            serialport.WriteLine(@"AT" + (char)(13));
            Thread.Sleep(200);

            serialport.WriteLine("AT+CMGF=1\r");
            Thread.Sleep(200);

            serialport.WriteLine("AT+CNMI=1,1,0,0\r");
            Thread.Sleep(200);

            // List ALL messages
            serialport.WriteLine("AT+CMGL=\"ALL\"\r");
            Thread.Sleep(10000); // 10 seconds

            // Read the response from the modem.
            string response = serialport.ReadExisting();

            // Parse the response to get the list of SMS messages.
            List<Sms> messages = ParseSmsMessages(response);

            List<Sms> messageList = new List<Sms>();
            // Display the list of SMS messages.
            foreach (Sms message in messages)
            {
                //Contruct our Rows
                messageList.Add(new Sms("1", message.message, message.messageId, message.sender, message.dateAndTime));
                
                try
                {
                    string connectionString = "Data Source=uphmc-dc33; Initial Catalog=ITWorksSMS; TrustServerCertificate=True; User ID=dalta; Password=dontshareit";

                    SqlConnection connection = new SqlConnection(connectionString);
                    // Open the connection
                    connection.Open();

                    SqlCommand command;
                    SqlDataAdapter adapter = new SqlDataAdapter();

                    command = connection.CreateCommand();

                    String sql = "TRUNCATE TABLE contacts";

                    command = new SqlCommand(sql, connection);
                    adapter.InsertCommand = new SqlCommand(sql, connection);
                    adapter.InsertCommand.ExecuteNonQuery();


                    // Close the connection
                    connection.Close();
                }
                catch (SqlException ex)
                {
                    // Handle any errors that occurred during the connection process
                    Console.WriteLine("An error occurred while connecting to the database: " + ex.Message);
                }
            }

            serialport.Close();

            return JsonSerializer.Serialize(messageList);
        }

        private List<Sms> ParseSmsContacts(string response)
        {
            int messageId = -1;
            string? date = null;
            string? message = null;
            string? sender = null;

            List<Sms> contacts = new List<Sms>();

            string[] number = response.Split(';');

            foreach (string phoneNumbers in number)
            {
                Console.WriteLine(sender = phoneNumbers.ToString());

            }
            contacts.Add(new Sms("1", message, messageId, sender, date));
            return contacts;
        }

        [HttpPost]
        [Route("SendMessage")]
        public string Send([FromBody] Sms sms)
        {
            serialport.Open();

            serialport.WriteLine(@"AT" + (char)(13));
            Thread.Sleep(1000);

            serialport.WriteLine("AT+CMGF=1\r");
            Thread.Sleep(1000);

            serialport.WriteLine("AT+CMGS=\"" + sms.phoneNumber + "\"\r\n");
            Thread.Sleep(1000);

            serialport.WriteLine(sms.message + "\x1A");
            Thread.Sleep(1000);

            string response = serialport.ReadExisting();

            List<Sms> contacts = ParseSmsContacts(response);

            List<Sms> contactsList = new List<Sms>();
            foreach (Sms contact in contacts)
            {
                contactsList.Add(new Sms(contact.phoneNumber));
            }

            serialport.Close();

            try
            {
                string connectionString = "Data Source=uphmc-dc33; Initial Catalog=ITWorksSMS; TrustServerCertificate=True; User ID=dalta; Password=dontshareit";

                SqlConnection connection = new SqlConnection(connectionString);
                // Open the connection
                connection.Open();

                SqlCommand command;
                SqlDataAdapter adapter = new SqlDataAdapter();

                command = connection.CreateCommand();

                String sql = "INSERT INTO sms_sent (contact_id, sms_message) VALUES (3, '" + sms.message + "')";

                command = new SqlCommand(sql, connection);
                adapter.InsertCommand = new SqlCommand(sql, connection);
                adapter.InsertCommand.ExecuteNonQuery();

                // Close the connection
                connection.Close();
            }
            catch (SqlException ex)
            {
                // Handle any errors that occurred during the connection process
                Console.WriteLine("An error occurred while connecting to the database: " + ex.Message);
            }

            return JsonSerializer.Serialize(contactsList);

        }

        [HttpPost]
        [Route("TriggerSend")]
        public string TriggerSend()
        {
            List<Dictionary<string, string>> queueMessages = GetQueue();
            System.Diagnostics.Debug.WriteLine(queueMessages);

            foreach (Dictionary<string, string> message in queueMessages)
            {
                try
                {
                    atSend(message["mobile_no"], message["sms_message"]);

                    System.Diagnostics.Debug.WriteLine("Message sent " + message["sms_message"] + " to " + message["mobile_no"]);

                    try
                    {
                        string connectionString = "Data Source=uphmc-dc33; Initial Catalog=ITWorksSMS; TrustServerCertificate=True; User ID=dalta; Password=dontshareit";

                        SqlConnection connection = new SqlConnection(connectionString);
                        
                        // Open the connection
                        connection.Open();

                        SqlCommand command;
                        SqlDataAdapter adapter = new SqlDataAdapter();

                        command = connection.CreateCommand();

                        String sql = "DELETE FROM sms_queue WHERE sms_id='" + message["sms_id"] + "';\n";
                        sql += "INSERT INTO sms_sent (sms_id, contact_id, mobile_no, sms_message) VALUES (" + message["sms_id"] + ", 0, '" + message["mobile_no"] + "', '"+ message["sms_message"] + "');";

                        command = new SqlCommand(sql, connection);
                        adapter.InsertCommand = new SqlCommand(sql, connection);
                        adapter.InsertCommand.ExecuteNonQuery();

                        // Close the connection
                        connection.Close();
                    }
                    catch (SqlException ex)
                    {
                        // Handle any errors that occurred during the connection process
                        Console.WriteLine("An error occurred while connecting to the database: " + ex.Message);
                    }

                }
                catch (Exception e)
                {
                    System.Diagnostics.Debug.WriteLine(e);
                }

            }

            return "200";
        }

        private void atSend(string number, string message) {
            serialport.Open();
            serialport.WriteLine(@"AT" + (char)(13));
            Thread.Sleep(1000);

            serialport.WriteLine("AT+CMGF=1\r");
            Thread.Sleep(1000);

            serialport.WriteLine("AT+CMGS=\"" + number + "\"\r\n");
            Thread.Sleep(1000);

            serialport.WriteLine(message + "\x1A");
            Thread.Sleep(1000);

            string response = serialport.ReadExisting();

            serialport.Close();
        }

        [HttpGet]
        [Route("SelectQueueMessages")]
        private List<Dictionary<string, string>> GetQueue()
        {
            string connectionString = "Data Source=uphmc-dc33; Initial Catalog=ITWorksSMS; TrustServerCertificate=True; User ID=dalta; Password=dontshareit";

            List<Dictionary<string, string>> rows = new List<Dictionary<string, string>>();
            Dictionary<string, string> column;

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                try
                {
                    // Open the connection
                    SqlCommand command = new SqlCommand("SELECT * FROM sms_queue", connection);
                    connection.Open();

                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            column = new Dictionary<string, string>();

                            column["sms_id"] = reader["sms_id"].ToString();
                            column["contact_id"] = reader["contact_id"].ToString();
                            column["mobile_no"] = reader["mobile_no"].ToString();
                            column["sms_message"] = reader["sms_message"].ToString();
                            
                            //Place the dictionary into the list
                            rows.Add(column); 
                        }
                    }
                }
                catch (SqlException ex)
                {
                    // Handle any errors that occurred during the connection process
                    Console.WriteLine("An error occurred while connecting to the database: " + ex.Message);
                }
                finally
                {
                    // Close the connection
                    connection.Close();
                }
            }

            return rows;
        }

        [HttpGet]
        [Route("TestDateTime")]
        public IEnumerable<SmsSent> Get()
        {
            return Enumerable.Range(1, 10).Select(index => new SmsSent
            {
                //Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)) //continuous date 
                //Date = DateOnly.FromDateTime(DateTime.Now) //date today
                date_created = DateTime.Now //for date and time 
            })
            .ToArray();
        }

        [HttpGet]
        [Route("SelectContacts")]
        public string Test()
        {
            string connectionString = "Data Source=uphmc-dc33; Initial Catalog=ITWorksSMS; TrustServerCertificate=True; User ID=dalta; Password=dontshareit";

            List<Dictionary<string, string>> rows = new List<Dictionary<string, string>>();
            Dictionary<string, string> column;

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                try
                {
                    // Open the connection
                    SqlCommand command = new SqlCommand("SELECT * FROM contacts", connection);
                    connection.Open();

                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            column = new Dictionary<string, string>();

                            column["employee_no"] = reader["employee_no"].ToString();
                            column["contact_lname"] = reader["contact_lname"].ToString();
                            column["contact_fname"] = reader["contact_fname"].ToString();

                            rows.Add(column); //Place the dictionary into the list
                        }
                    }
                }
                catch (SqlException ex)
                {
                    // Handle any errors that occurred during the connection process
                    Console.WriteLine("An error occurred while connecting to the database: " + ex.Message);
                }
                finally
                {
                    // Close the connection
                    connection.Close();
                }
            }

            return JsonSerializer.Serialize(rows);
        }
    }
}