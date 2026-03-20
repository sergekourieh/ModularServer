using System.Data.SQLite;

namespace Server {
    public class LuaDatabase {
        public static SQLiteConnection? dbConnect(string dbFile) {
            try {
                SQLiteConnection con = new SQLiteConnection($"Data Source={dbFile};Version=3;");
                con.Open();
                Server.print("Connected to the database.");
                return con;
            } catch (Exception ex) {
                Server.print(ex.Message, ConsoleColor.DarkMagenta);
            }
            return null;
        }
        public static bool dbExec (SQLiteConnection con, string query, params object[] parameters) {
            if (con == null)
                return false;
            try {
                using (var cmd = new SQLiteCommand(query, con)) {
                    if (parameters != null) {
                        SQLiteParameter[] parms = new SQLiteParameter[parameters.Length];
                        for (int i=0; i<parameters.Length; i++) {
                            parms[i] = new SQLiteParameter($"@{i}", parameters[i]);
                        }
                        cmd.Parameters.AddRange(parms);
                    }
                    int result = cmd.ExecuteNonQuery();
                    if (result>0)
                        return true;
                    return false;
                }
            } catch (Exception ex) {
                Server.print($"Error executing SQL: '{ex.Message}'", ConsoleColor.DarkMagenta);
                if (ex.StackTrace != null)
                    Server.print(ex.StackTrace);
            }
            return false;
        }
        public static List<Dictionary<string, object>> dbQuery (SQLiteConnection con, string query, params object[] parameters) {
            if (con == null)
                return new List<Dictionary<string, object>>();
            var results = new List<Dictionary<string, object>>();
            if (con == null)
                return results;
            try {
                using (var cmd = new SQLiteCommand(query, con)) {
                    for (int i = 0; i < parameters.Length; i++) {
                        cmd.Parameters.AddWithValue($"@p{i}", parameters[i]);
                    }
                    using (var reader = cmd.ExecuteReader()) {
                        while (reader.Read()) {
                            var row = new Dictionary<string, object>();
                            for (int i=0; i<reader.FieldCount; i++) {
                                row[reader.GetName(i)] = reader[i];
                            }
                            results.Add(row);
                        }
                    }
                }
            } catch (NLua.Exceptions.LuaException ex) {
                Server.print($"Database query error: '{ex.Message}'", ConsoleColor.DarkRed);

            }
            return results;
        }
        public static void dbClose(SQLiteConnection con) {
            if (con == null)
                return;
            con.Close();
            Server.print("Database connection closed.");
        }
    }
}