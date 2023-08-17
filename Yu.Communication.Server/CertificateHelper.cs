using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Yu.Communication.Server
{
    public class CertData
    {
        public string CertName { get; set; }
        public string CertFile { get; set; }
        public string CertPwd { get; set; }
        public bool UseSsl { get { return !string.IsNullOrWhiteSpace(CertName) || !string.IsNullOrWhiteSpace(CertFile); } }
    }
    public class PortCert: CertData
    {
        public int Port { get; set; }
        public int PortSsl { get; set; }
    }
    public class CertificateHelper
    {
        #region 是否允许该客户端与未经身份验证的服务器通信
        public static RemoteCertificateValidationCallback ValidateRemoteCertificate2= (a, b, c, d) => true;
        public static bool ValidateRemoteCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None) return true;
            Console.WriteLine($"证书 Certificate error: {sslPolicyErrors}[{certificate}]");
            if (sslPolicyErrors == SslPolicyErrors.RemoteCertificateChainErrors)
            {
                var chainInfo = chain == null ? null : $"{chain.ChainStatus[0].Status},{chain.ChainStatus[0].StatusInformation}";
                Console.WriteLine($"The X509Chain.ChainStatus returned an array of X509ChainStatus objects containing error information.[{chainInfo}]");
            }
            else if (sslPolicyErrors == SslPolicyErrors.RemoteCertificateNameMismatch)
            {
                Console.WriteLine("There was a mismatch of the name on a certificate.");
            }
            else if (sslPolicyErrors == SslPolicyErrors.RemoteCertificateNotAvailable)
            {
                Console.WriteLine("No certificate was available.");
            }
            else
            {
                Console.WriteLine("SSL Certificate Validation Error!");
            }
            return false;//不允许该客户端与未经身份验证的服务器通信。
        }
        #endregion
        #region 读取ssl证书
        /// <summary>
        /// 查找证书
        /// </summary>
        /// <param name="certName">证书名：CN=*.yylyhl.com / CN=*.domain.com, O=xxxx有限公司, L=杭州市, S=浙江省, C=CN</param>
        /// <param name="validOnly">仅取有效的</param>
        /// <returns>证书</returns>
        internal static X509Certificate2? GetCertificateFromStore(string certName, bool validOnly = true)
        {
            if (string.IsNullOrWhiteSpace(certName)) return default;
            X509Certificate2Collection signingCerts = GetCertificatesFromStore(certName, validOnly = true);
            return signingCerts.Any() ? signingCerts[0] : null;
        }
        /// <summary>
        /// 查找证书集合
        /// </summary>
        /// <param name="certName">证书名：CN=*.yylyhl.com / CN=*.domain.com, O=xxxx有限公司, L=杭州市, S=浙江省, C=CN</param>
        /// <param name="validOnly">仅取有效的</param>
        /// <returns>证书</returns>
        public static X509Certificate2Collection GetCertificatesFromStore(string certName, bool validOnly = true)
        {
            if (string.IsNullOrWhiteSpace(certName)) return new X509Certificate2Collection();
            var store = new X509Store(StoreName.My);
            try
            {
                store.Open(OpenFlags.ReadOnly);
                X509Certificate2Collection signingCerts = FindCertificate(store.Certificates, certName, validOnly);
                if (!signingCerts.Any())
                {
                    store = new X509Store(StoreName.Root);
                    store.Open(OpenFlags.ReadOnly);
                    signingCerts = FindCertificate(store.Certificates, certName, validOnly);
                }
                return signingCerts;
            }
            finally
            {
                store?.Close();
            }
        }
        private static X509Certificate2Collection FindCertificate(X509Certificate2Collection certificates, string certName, bool validOnly = true)
        {
            if (certificates == null) return new X509Certificate2Collection();
            //CN=*.domain.com, O=xxxx有限公司, L=杭州市, S=浙江省, C=CN
            var curCertificates = certificates.Find(X509FindType.FindBySubjectDistinguishedName, certName, validOnly);
            //*.domain.com
            certificates = curCertificates.Any() ? curCertificates : certificates.Find(X509FindType.FindBySubjectName, certName, validOnly);
            certificates = certificates.Find(X509FindType.FindByTimeValid, DateTime.Now, validOnly);
            return certificates;
        }
        /// <summary>
        /// 获取证书
        /// </summary>
        /// <param name="certFile">证书名</param>
        /// <param name="certPwd">证书密码</param>
        internal static X509Certificate2? GetCertificate(string certFile, string? certPwd = null)
        {
            //CN=*.yylyhl.com//CN=*.yylyhl.com, O=xxxx有限公司, L=杭州市, S=浙江省, C=CN
            if (string.IsNullOrWhiteSpace(certFile)) return default;
            var certificate = new X509Certificate2(certFile, certPwd, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
            return certificate;
        }
        /// <summary>
        /// 获取证书Hash
        /// </summary>
        /// <param name="certFile">证书名</param>
        /// <param name="certPwd">证书密码</param>
        internal static byte[]? GetCertificateHash(string certFile, string? certPwd = null)
        {
            var certificate = GetCertificate(certFile, certPwd);
            if (certificate == null) return default;
            var certificateHash = certificate.GetCertHash();//证书哈希
            //var certificateHash = certificate.Export(X509ContentType.Pfx);
            return certificateHash;
        }
        /// <summary>
        /// 获取证书名
        /// </summary>
        /// <param name="certFile">证书名</param>
        /// <param name="certPwd">证书密码</param>
        internal static string? GetCertificateName(string certFile, string? certPwd = null)
        {
            var certificate = GetCertificate(certFile, certPwd);
            return certificate?.Subject;
            //if (certificate == null) return default;
            //var store = new X509Store(StoreName.AuthRoot, StoreLocation.LocalMachine);
            //store.Open(OpenFlags.OpenExistingOnly | OpenFlags.ReadWrite);
            //store.Add(certificate);
            //var certificateName = store.Name;
            //store.Close();
            //return certificateName;
        }
        #endregion
    }
}
