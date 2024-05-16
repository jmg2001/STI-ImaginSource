using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace STI
{
    public partial class InputDlg2 : Form
    {
        public string units { get; set; }
        public double cameraHeight { get; set; }

        public InputDlg2(string unit)
        {
            units = unit;
            InitializeComponent();
        }

        public void InputDlg2_Load(object sender, EventArgs e)
        {
            txtUnits.Text = units;
        }

        private void btnContinue_Click(object sender, EventArgs e)
        {
            double height = 0;
            // Intentar convertir el texto del TextBox a un número entero
            if (double.TryParse(inputHeight.Text, out height))
            {
                height = validate(height);

                DialogResult result = MessageBox.Show($"You are going to perform a calibration with {inputHeight.Text} {units} height. Do you want to continue?", "Warning", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);

                // Verificar la opción seleccionada por el usuario
                if (result == DialogResult.OK)
                {
                    cameraHeight = height;
                    // Si el usuario elige "Sí", continuar con la acción deseada
                    DialogResult = DialogResult.OK;
                }
                else
                {
                    DialogResult = DialogResult.Cancel;
                    // Si el usuario elige "No", puedes hacer algo o simplemente salir
                    MessageBox.Show("Operation canceled");

                    this.Close();
                }
            }
            else
            {
                // Manejar el caso en que el texto no sea un número válido
                MessageBox.Show("Use a valid number", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private double validate(double height)
        {
            if (units == "mm")
            {
                return (int)height;
            }
            else
            {
                return height;
            }
        }
    }
}
