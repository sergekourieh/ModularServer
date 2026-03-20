using System.Data.SQLite;

namespace Server {
    public class Database {
        #region Attributes
        private static string fName = "database.db";
        #endregion
        public static void initiateDatabase() {
            exec("CREATE TABLE IF NOT EXISTS globalValues (id INTEGER PRIMARY KEY, key TEXT, value TEXT)");
        }
        public static SQLiteConnection connect() {
            string cs = $"Data Source={fName}";
            var con = new SQLiteConnection(cs);
            con.Open();
            return con;
        }
        public static bool exec(string sql, params object[] parameters) {
            using var con = connect();
            if (con == null)
                return false;
            try {
                using var cmd = new SQLiteCommand(sql, con);
                if (parameters != null) {
                    SQLiteParameter[] parms = new SQLiteParameter[parameters.Length];
                    for (int i=0; i<parameters.Length; i++) {
                        parms[i] = new SQLiteParameter($"@{i}", parameters[i]);
                    }
                    cmd.Parameters.AddRange(parms);
                }
                int result = cmd.ExecuteNonQuery();
                con.Close();
                if (result>0)
                    return true;
                return false;
            } catch (Exception _ex) {
                Server.print($"Exception found @Database.exec, exception: {_ex}\nWith SQL: {sql}", ConsoleColor.DarkMagenta);
                con.Close();
            }
            return false;
        }
        public static Dictionary<object, Dictionary<string, object>> query(string sql, params object[] parameters) {
            Dictionary<object, Dictionary<string, object>> data = new Dictionary<object, Dictionary<string, object>>();
            using var con = connect();
            if (con == null)
                return new Dictionary<object, Dictionary<string, object>>();
            try {
                using var cmd = new SQLiteCommand(sql, con);
                if (parameters != null) {
                    SQLiteParameter[] parms = new SQLiteParameter[parameters.Length];
                    for (int i=0; i<parameters.Length; i++) {
                        parms[i] = new SQLiteParameter($"@{i}", parameters[i]);
                    }
                    cmd.Parameters.AddRange(parms);
                }
                using SQLiteDataReader rdr = cmd.ExecuteReader();
                if (!rdr.HasRows) {
                    goto skip;
                }
                while(rdr.Read()) {
                    Dictionary<string, object> d = new Dictionary<string, object>();
                    for(int i=1; i<rdr.FieldCount; i++) {
                        d.Add(rdr.GetName(i), rdr.GetValue(i));
                    }
                    data.Add(rdr.GetValue(0), d);
                }
            } catch (Exception _ex) {
                Server.print($"Exception found @Database.exec, exception: {_ex}\nWith SQL: {sql}", ConsoleColor.DarkMagenta);
            }
            skip:
            con.Close();
            return data;
        }
    }
}