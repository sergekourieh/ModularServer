namespace Server {
    public class Utility {
        public static long getTick() {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
        public static string getTickCount() {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        }
        public static string printTick() {
            long timestampLong = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            DateTime dateTime = DateTimeOffset.FromUnixTimeMilliseconds(timestampLong).DateTime.ToLocalTime();
            return dateTime.ToString("HH:mm:ss");
        }
        public static string tickToString(object timestamp) {
            long timestampLong = Convert.ToInt64(timestamp);
            DateTime dateTime = DateTimeOffset.FromUnixTimeMilliseconds(timestampLong).DateTime.ToLocalTime();
            return dateTime.ToString("yyyy/MM/dd hh:mm tt");
        }
        public static bool ContainsSC(string input) { //Contains Special Characters?
            string pattern = @"[^a-zA-Z0-9\s]";
            return System.Text.RegularExpressions.Regex.IsMatch(input, pattern);
        }
    }
}