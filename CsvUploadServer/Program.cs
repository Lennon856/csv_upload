
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Data.SQLite;

class Program
{
    static void Main()
    {
        // Create an HTTP listener 
        HttpListener listener = new HttpListener();
        listener.Prefixes.Add("http://localhost:5000/");
        listener.Start();
        Console.WriteLine("Server started at http://localhost:5000/");

        // Main loop: keep handling requests forever
        while (true)
        {
            var context = listener.GetContext();        
            var req = context.Request;                  
            var resp = context.Response;                

            // If the request is a GET (user opening the page)
            if (req.HttpMethod == "GET")
            {
                // Serve the upload form HTML (with CSS styling)
                string html = @"<!DOCTYPE html>
<html lang=""en"">
<head>
  <meta charset=""UTF-8"">
  <title>CSV Upload</title>
  <style>
    body { background: #f7f7f7; font-family: Arial, sans-serif; margin:0; padding:0; }
    .container { width:400px; margin:80px auto; background:#fff; border-radius:8px;
                 box-shadow:0 0 10px rgba(0,0,0,0.1); padding:20px; }
    .container h2 { text-align:center; color:#333; margin-bottom:20px; }
    label { display:block; margin-bottom:5px; color:#333; font-weight:bold; }
    input[type=file] { width:100%; padding:8px; border:1px solid #ccc; border-radius:4px; margin-bottom:15px; }
    input[type=file]::file-selector-button {
      background:#4CAF50; color:white; border:none; border-radius:4px; padding:6px 12px; cursor:pointer;
    }
    input[type=file]::file-selector-button:hover { background:#45a049; }
    .buttons { text-align:center; margin-top:10px; }
    button { padding:10px 20px; border:none; border-radius:4px; background:#4CAF50; color:white; font-size:14px; cursor:pointer; }
    button:hover { background:#45a049; }
    .message { margin-top:20px; padding:10px; border-radius:4px; }
    .success { background-color:#e2f7e2; color:#2e7d32; }
    .error { background-color:#fce4e4; color:#c62828; }
  </style>
</head>
<body>
  <div class=""container"">
    <h2>Upload CSV File</h2>
    <form method=""post"" enctype=""multipart/form-data"" action=""/upload"">
      <label for=""csvFile"">Choose CSV file:</label>
      <input type=""file"" id=""csvFile"" name=""csvFile"" accept="" .csv"" required />
      <div class=""buttons"">
        <button type=""submit"">Upload</button>
      </div>
    </form>
  </div>
</body>
</html>";
                // Convert HTML string to bytes (UTF-8)
                byte[] buf = Encoding.UTF8.GetBytes(html);
                // Tell browser what content type is being sent
                resp.ContentType = "text/html";
                resp.ContentLength64 = buf.Length;
                // Write bytes to the output stream
                resp.OutputStream.Write(buf, 0, buf.Length);
                resp.OutputStream.Close();  // Done sending response
            }
            // If the request is a POST to /upload (form submission)
            else if (req.HttpMethod == "POST" && req.Url.AbsolutePath == "/upload")
            {
                string errorMsg = null;    
                string successMsg = null;  // will hold success message if done

                try
                {
                    
                    Directory.CreateDirectory("output");
                   
                    string uploadPath = Path.Combine("output", "uploaded.csv");
                    using (var fs = new FileStream(uploadPath, FileMode.Create, FileAccess.Write))
                    {
                        req.InputStream.CopyTo(fs);
                    }

                    // Read that file back entirely as text
                    string allText = File.ReadAllText(uploadPath);
                    // Find where headers end (blank line) and drop before portion
                    int start = allText.IndexOf("\r\n\r\n");
                    if (start >= 0) allText = allText.Substring(start + 4);
                    // Find the boundary marker at the end and drop after
                    int end = allText.LastIndexOf("------");
                    if (end >= 0) allText = allText.Substring(0, end);
                    // Trim whitespace
                    allText = allText.Trim();
                    // Overwrite upload file with just the CSV content
                    File.WriteAllText(uploadPath, allText);

                    // Now import into SQLite database
                    string connStr = "Data Source=output/test.db;Version=3;";
                    using (var conn = new SQLiteConnection(connStr))
                    {
                        conn.Open();
                        // Drop and recreate a fresh table
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

                        // Bulk insert using transaction & prepared command
                        using (var tx = conn.BeginTransaction())
                        using (var insertCmd = new SQLiteCommand(
                            "INSERT INTO csv_import VALUES (@Id,@Name,@Surname,@Initials,@Age,@DateOfBirth)",
                            conn, tx))
                        {
                            // Add parameter placeholders just once
                            var pId = insertCmd.Parameters.Add("@Id", System.Data.DbType.Int32);
                            var pName = insertCmd.Parameters.Add("@Name", System.Data.DbType.String);
                            var pSurname = insertCmd.Parameters.Add("@Surname", System.Data.DbType.String);
                            var pInitials = insertCmd.Parameters.Add("@Initials", System.Data.DbType.String);
                            var pAge = insertCmd.Parameters.Add("@Age", System.Data.DbType.Int32);
                            var pDob = insertCmd.Parameters.Add("@DateOfBirth", System.Data.DbType.String);

                            bool skipHeader = true;
                            // Loop through each line in CSV
                            foreach (var line in allText.Split('\n'))
                            {
                                if (string.IsNullOrWhiteSpace(line)) continue;
                                if (skipHeader) { skipHeader = false; continue; }

                                var cols = line.Split(',');
                                if (cols.Length < 6) continue;

                                // Parse and set parameter values
                                pId.Value = int.Parse(cols[0]);
                                pName.Value = cols[1];
                                pSurname.Value = cols[2];
                                pInitials.Value = cols[3];
                                pAge.Value = int.Parse(cols[4]);
                                pDob.Value = cols[5].Trim();

                                insertCmd.ExecuteNonQuery();
                            }

                            tx.Commit();  // commit all inserts at once
                        }
                    }

                    successMsg = "CSV uploaded and inserted into SQLite successfully!";
                }
                catch (Exception ex)
                {
                    // If anything fails, set error message
                    errorMsg = "Error processing upload: " + ex.Message;
                }

                // Build a styled result page (HTML) showing success or error
                bool succeeded = errorMsg == null;
                string resultHtml = $@"<!DOCTYPE html>
<html lang=""en"">
<head>
  <meta charset=""UTF-8"">
  <title>Upload Result</title>
  <style>
    body {{ background: #f7f7f7; font-family: Arial, sans-serif; margin:0; padding:0; }}
    .container {{ width:400px; margin:80px auto; background:#fff; border-radius:8px;
                 box-shadow:0 0 10px rgba(0,0,0,0.1); padding:20px; }}
    .container h2 {{ text-align:center; color:#333; margin-bottom:20px; }}
    .message {{ margin-top:20px; padding:10px; border-radius:4px; }}
    .success {{ background-color:#e2f7e2; color:#2e7d32; }}
    .error {{ background-color:#fce4e4; color:#c62828; }}
    .buttons {{ text-align:center; margin-top:15px; }}
    .buttons button {{ padding:10px 20px; border:none; border-radius:4px; background:#4CAF50; color:white; font-size:14px; cursor:pointer; }}
    .buttons button:hover {{ background:#45a049; }}
  </style>
</head>
<body>
  <div class=""container"">
    <h2>Upload Result</h2>
    <div class=""message {(succeeded ? "success" : "error")}"">
      {(succeeded ? successMsg : errorMsg)}
    </div>
    <div class=""buttons"">
      <button onclick=""window.location.href='/'"">Back to Upload</button>
    </div>
  </body>
</html>";

                // Send the result HTML back to user
                byte[] outBuf = Encoding.UTF8.GetBytes(resultHtml);
                resp.ContentType = "text/html; charset=UTF-8";
                resp.ContentLength64 = outBuf.Length;
                resp.OutputStream.Write(outBuf, 0, outBuf.Length);
                resp.OutputStream.Close();
            }
        }
    }
}

