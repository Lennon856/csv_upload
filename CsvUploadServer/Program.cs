
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Data.SQLite;

class Program
{
    static void Main()
    {
        // 1. Start a simple web server on localhost:5000
        HttpListener listener = new HttpListener();
        listener.Prefixes.Add("http://localhost:5000/");
        listener.Start();
        Console.WriteLine("Server started at http://localhost:5000/");
        Console.WriteLine("Open that address in your browser to see the form.");

        while (true)
        {
            HttpListenerContext context = listener.GetContext();
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;

            if (request.HttpMethod == "GET")
            {
                // 2. Serve HTML form (upload.html must be in same folder as EXE)
                string htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "upload.html");
                Console.WriteLine("Serving HTML: " + htmlPath);

                string html = File.ReadAllText(htmlPath);    // Read the upload page off disk
                byte[] buffer = Encoding.UTF8.GetBytes(html);  // Encode HTML to bytes


                response.ContentType = "text/html";
                response.OutputStream.Write(buffer, 0, buffer.Length);
                response.OutputStream.Close();
            }
            else if (request.HttpMethod == "POST")
            {
                // 3. Read uploaded file data from request
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                {
                    string body = reader.ReadToEnd();

                    // Find where file content starts
                    string marker = "\r\n\r\n"; // blank line after headers
                    int start = body.IndexOf(marker);
                    if (start < 0) start = 0;
                    else start += marker.Length;

                    // Find the boundary (end of file content)
                    int end = body.LastIndexOf("------");
                    if (end < 0) end = body.Length;

                    // Extract just the CSV content
                    string csvData = body.Substring(start, end - start).Trim();

                    // Save uploaded CSV to file (optional)
                    File.WriteAllText("uploaded.csv", csvData);

                    // 4. Insert into SQLite
                    string connectionString = "Data Source=output/test.db;Version=3;";
                    Directory.CreateDirectory("output");

                    using (var conn = new SQLiteConnection(connectionString))
                    {
                        conn.Open();

                        // Create a fresh table
                        string createTable = @"DROP TABLE IF EXISTS csv_import;
                                               CREATE TABLE csv_import(
                                                 Id INTEGER,
                                                 Name TEXT,
                                                 Surname TEXT,
                                                 Initials TEXT,
                                                 Age INTEGER,
                                                 DateOfBirth TEXT
                                               );";
                        using (var cmd = new SQLiteCommand(createTable, conn))
                        {
                            cmd.ExecuteNonQuery();
                        }

                        // Prepare insert
                        using (var tx = conn.BeginTransaction())
                        using (var insertCmd = new SQLiteCommand(
                            "INSERT INTO csv_import VALUES (@Id,@Name,@Surname,@Initials,@Age,@DateOfBirth)", conn, tx))
                        {
                            var pId = insertCmd.Parameters.Add("@Id", System.Data.DbType.Int32);
                            var pName = insertCmd.Parameters.Add("@Name", System.Data.DbType.String);
                            var pSurname = insertCmd.Parameters.Add("@Surname", System.Data.DbType.String);
                            var pInitials = insertCmd.Parameters.Add("@Initials", System.Data.DbType.String);
                            var pAge = insertCmd.Parameters.Add("@Age", System.Data.DbType.Int32);
                            var pDob = insertCmd.Parameters.Add("@DateOfBirth", System.Data.DbType.String);

                            bool skipHeader = true;
                            foreach (var line in csvData.Split('\n'))
                            {
                                if (string.IsNullOrWhiteSpace(line)) continue;
                                if (skipHeader) { skipHeader = false; continue; }

                                var cols = line.Split(',');
                                if (cols.Length < 6) continue;

                                pId.Value = int.Parse(cols[0]);
                                pName.Value = cols[1];
                                pSurname.Value = cols[2];
                                pInitials.Value = cols[3];
                                pAge.Value = int.Parse(cols[4]);
                                pDob.Value = cols[5];

                                insertCmd.ExecuteNonQuery();
                            }
                            tx.Commit();
                        }
                    }
                }

                // 5. Send confirmation to browser
                string msg = "   CSV uploaded and inserted into SQLite successfully!";
                byte[] buffer = Encoding.UTF8.GetBytes(msg);
                response.ContentType = "text/plain";
                response.OutputStream.Write(buffer, 0, buffer.Length);
                response.OutputStream.Close();
            }
        }
    }
}

