using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace RegistServerDemo
{
    class Program
    {
        static void Main(string[] args)
        {
            string ipString = GetIPAddress();
            if (string.IsNullOrEmpty(ipString))
            {
                Environment.Exit(0);
            }

            IPAddress ipAdd = IPAddress.Parse(ipString);

            //Listenするポート番号
            int port = 8989;

            //TcpListenerオブジェクトを作成する
            TcpListener listener = new TcpListener(ipAdd, port);

            //Listenを開始する
            listener.Start();
            Console.WriteLine("Listenを開始しました({0}:{1})。",
                ((IPEndPoint)listener.LocalEndpoint).Address,
                ((IPEndPoint)listener.LocalEndpoint).Port);

            while (true)
            {
                bool disconnected = false;

                //接続要求があったら受け入れる
                using (var client = listener.AcceptTcpClient())
                {
                    Console.WriteLine("クライアント({0}:{1})と接続しました。",
                    ((IPEndPoint)client.Client.RemoteEndPoint).Address,
                    ((IPEndPoint)client.Client.RemoteEndPoint).Port);

                    //NetworkStreamを取得
                    using (NetworkStream ns = client.GetStream())
                    {
                        //ns.ReadTimeout = 10000;
                        //ns.WriteTimeout = 10000;

                        //クライアントから送られたデータを受信する
                        using (var ms = new MemoryStream())
                        {
                            byte[] resBytes = new byte[256];
                            int resSize;

                            do
                            {
                                do
                                {
                                    // データの一部を受信する
                                    resSize = ns.Read(resBytes, 0, resBytes.Length);
                                    if (resSize == 0 || (resSize == 2 && resBytes[0] == 0x58))
                                    {
                                        /*
                                         * resSize == 0: クライアント側のCloseの場合、0でもns.Readが通るため
                                         * resSize == 2: クライアント側からの明示的な切断コマンド。0x58 == 'X'
                                         */
                                        disconnected = true;
                                        Console.WriteLine("クライアントが切断しました。");
                                        break;
                                    }
                                    // 受信したデータを蓄積する
                                    ms.Write(resBytes, 0, resSize);
                                    // まだ読み取れるデータがあるか、データの最後が\nでない時は、受信を続ける
                                } while (ns.DataAvailable || resBytes[resSize - 1] != '\n');

                                // 受信したデータを文字列に変換
                                string resMsg = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
                                resMsg = resMsg.TrimEnd('\n');
                                Console.WriteLine(resMsg);

                                string sendMsg = ProcessReceivedData(resMsg);

                                if (!disconnected)
                                {
                                    byte[] sendBytes = Encoding.UTF8.GetBytes(sendMsg + '\n');
                                    ns.Write(sendBytes, 0, sendBytes.Length);
                                    Console.WriteLine(sendMsg);
                                }

                                // 受信用メモリストリームをクリア
                                ms.SetLength(0);

                            } while (!disconnected);

                        }
                    }
                }
                Console.WriteLine("クライアントとの接続を閉じました。");
            }
        }

        private static string GetIPAddress()
        {
            string addr_ip = "";

            try
            {
                string hostname = Dns.GetHostName();

                //ホスト名からIPアドレスを取得
                IPAddress[] addr_arr = Dns.GetHostAddresses(hostname);

                //探す
                foreach (IPAddress addr in addr_arr)
                {
                    string addr_str = addr.ToString();

                    //IPv4 && localhostでない
                    if (addr_str.IndexOf(".") > 0 && !addr_str.StartsWith("127."))
                    {
                        addr_ip = addr_str;
                        break;
                    }
                }
            }
            catch (Exception)
            {
                addr_ip = "";
            }

            return addr_ip;
        }

        private static string ProcessReceivedData(string data)
        {
            string[] strs = data.Split(',');
            if (strs.Length != 3 || !strs[0].Equals("RRC"))
            {
                return "BADFORMAT";
            }
            else
            {
                string rawSN = DecodeSerialNumber(strs[2]);
                if (string.IsNullOrEmpty(rawSN))
                {
                    return "BADSN";
                }

                byte[] compressedKey = HexStringToByteArray(strs[1].Replace("-", string.Empty));
                if (compressedKey == null)
                {
                    return "BADINFO01";
                }

                string decompressedKey = DataOperation.DecompressToStr(compressedKey);
                if (string.IsNullOrEmpty(decompressedKey))
                {
                    return "BADINFO02";
                }

                string sysInfo = CryptoMethod.Decrypt(decompressedKey);
                if (string.IsNullOrEmpty(sysInfo))
                {
                    return "BADINFO03";
                }

                byte[] input = Encoding.ASCII.GetBytes(sysInfo + rawSN);
                var sb = new StringBuilder("");
                using (SHA256 sha = new SHA256CryptoServiceProvider())
                {
                    byte[] hash_sha256 = sha.ComputeHash(input);
                    for (int i = 0; i < hash_sha256.Length; i++)
                    {
                        sb.Append(string.Format("{0:X2}", hash_sha256[i]));
                    }
                    int length = sb.Length;
                    for (int i = 4; i < length + length / 4 - 1; i += 5)
                    {
                        _ = sb.Insert(i, "-");
                    }
                }

                return sb.ToString();
            }
        }

        private static string DecodeSerialNumber(string encodedSN)
        {
            string str = "";

            if (!string.IsNullOrEmpty(encodedSN) && encodedSN.Length == 8)
            {
                int l = encodedSN[0] - 48;
                int p = (10 + encodedSN[1] - 48 - 2) % 10;
                int q = (10 + encodedSN[2] - 48 - 4) % 10;
                int r = (10 + encodedSN[3] - 48 - 1) % 10;
                int s = (10 + encodedSN[4] - 48 - 3) % 10;
                int t = (10 + encodedSN[5] - 48 - 5) % 10;
                int u = (10 + encodedSN[6] - 48 - 7) % 10;
                int m = encodedSN[7] - 48;
                int sum1 = 0;
                for (int i = 1; i < 7; i++)
                {
                    sum1 += (encodedSN[i] - 48);
                }
                int sum2 = l + sum1;

                if (l != sum1 % 10 || m != sum2 % 10)
                {
                    str = "";
                }
                else
                {
                    str = string.Format("{0}{1}{2}{3}{4}{5}", p, q, r, s, t, u);
                }
            }

            return str;
        }

        private static byte[] HexStringToByteArray(string input)
        {
            byte[] result = new byte[input.Length / 2];

            int cur = 0;

            for (int i = 0; i < input.Length; i += 2)
            {
                try
                {
                    string w = input.Substring(i, 2);
                    try
                    {
                        result[cur] = Convert.ToByte(w, 16);
                    }
                    catch (FormatException)
                    {
                        return null;
                    }
                }
                catch (ArgumentOutOfRangeException)
                {
                    return null;
                }
                cur++;
            }

            return result;
        }
    }

    /// <summary>
    /// AESを用いて暗号化および復号化するメソッドを提供します
    /// </summary>
    public static class CryptoMethod
    {
        private static readonly string DefaultAesIV = @"DD5PCSoftware001";
        private static readonly string DefaultAesKey = @"Higashitateuricho206KamigyokuKYT";
        private static readonly int KeySize = 256;
        private static readonly int BlockSize = 128;

        /// <summary>
        /// 固定の共有キーと初期化ベクターを用いて文字列を暗号化します
        /// </summary>
        /// <param name="value">暗号化の対象となる文字列</param>
        /// <returns></returns>
        public static string Encrypt(string value)
        {
            string base64value;

            using (AesManaged aes = new AesManaged())
            {
                aes.KeySize = KeySize;
                aes.BlockSize = BlockSize;
                aes.Key = Encoding.UTF8.GetBytes(DefaultAesKey);
                aes.IV = Encoding.UTF8.GetBytes(DefaultAesIV);

                byte[] byteValue = Encoding.UTF8.GetBytes(value);
                ICryptoTransform encryptor = aes.CreateEncryptor();
                byte[] encryptValue = encryptor.TransformFinalBlock(byteValue, 0, byteValue.Length);
                base64value = Convert.ToBase64String(encryptValue);
            }

            return base64value;
        }

        /// <summary>
        /// 固定の共有キーと初期化ベクターを用いて暗号化された文字列を復号化します
        /// </summary>
        /// <param name="encryptValue">暗号化された文字列</param>
        /// <returns></returns>
        public static string Decrypt(string encryptValue)
        {
            string stringValue;

            using (AesManaged aes = new AesManaged())
            {
                aes.KeySize = KeySize;
                aes.BlockSize = BlockSize;
                aes.Key = Encoding.UTF8.GetBytes(DefaultAesKey);
                aes.IV = Encoding.UTF8.GetBytes(DefaultAesIV);

                try
                {
                    byte[] byteValue = Convert.FromBase64String(encryptValue);
                    ICryptoTransform decryptor = aes.CreateDecryptor();
                    byte[] decryptValue = decryptor.TransformFinalBlock(byteValue, 0, byteValue.Length);
                    stringValue = Encoding.UTF8.GetString(decryptValue);
                }
                catch (FormatException)
                {
                    stringValue = "";
                }
            }

            return stringValue;
        }
    }
}