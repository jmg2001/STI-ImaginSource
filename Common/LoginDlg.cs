using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using System;
using System.Security.Cryptography;

namespace STI
{
    public partial class LoginDlg : Form
    {
        string user;
        string password;

        string saltString = "BTl6wFfeY0E0sMiiWxwCMA==";
        string hashString = "+gFCYNRnjTh6zoI4iUj5TP0Qxc4=";

        byte[] salt;
        byte[] storedHash;

        public LoginDlg(string user)
        {
            this.user = user;

            // Convertir salt y hash de cadena base64 a bytes
            salt = Convert.FromBase64String(saltString);
            storedHash = Convert.FromBase64String(hashString);


            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {

        }

        private void button1_Click(object sender, EventArgs e)
        {
            // Convertir la contraseña ingresada por el usuario en bytes
            string inputPassword = txtPassword.Text; // Supongamos que obtienes la contraseña del usuario
            byte[] inputPasswordBytes = System.Text.Encoding.UTF8.GetBytes(inputPassword);

            // Generar un nuevo hash a partir de la contraseña ingresada y el salt almacenado
            var hasher = new Rfc2898DeriveBytes(inputPasswordBytes, salt, 10000);
            byte[] inputHash = hasher.GetBytes(20); // Generar el hash de la contraseña ingresada

            // Comparar el hash generado con el hash almacenado en la base de datos
            bool isValidPassword = CompareHashes(inputHash, storedHash);
            Console.WriteLine("La contraseña es válida: " + isValidPassword);

            if (txtUser.Text == user)
            {
                if (isValidPassword)
                {
                    this.DialogResult = DialogResult.OK;
                }
                else
                {
                    MessageBox.Show("Wrong Password");
                    this .DialogResult = DialogResult.Cancel;
                }
            }
            else
            {
                MessageBox.Show("Wrong User");
                this.DialogResult = DialogResult.Cancel;
            }
            this.Close();
        }

        // Función para comparar dos hashes byte a byte
        public static bool CompareHashes(byte[] hash1, byte[] hash2)
        {
            if (hash1.Length != hash2.Length)
                return false;

            for (int i = 0; i < hash1.Length; i++)
            {
                if (hash1[i] != hash2[i])
                    return false;
            }
            return true;
        }
    }
}
