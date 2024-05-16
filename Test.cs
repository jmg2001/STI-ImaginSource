using CsvHelper;
using EasyModbus;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ic4;
using ic4.WinForms;


[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

namespace STI
{
    public partial class Test : Form
    {
        

        //// Variables globales
        public RECT UserROI = new RECT();
        long[] Histogram = new long[256];


        // Creadas por mi
        bool authenticated = false;
        string user = "admin";

        bool freezeFrame = false;

        Properties.Settings settings = new Properties.Settings();

        // Datos de la lente
        double lenF = 4;
        double lenWidth = 4.8;
        double lenHeight = 8.16;

        // Color de la tortilla en la imagen binarizada
        int tortillaColor = 1; // 1 - Blanco, 0 - Negro
        int backgroundColor = 0;

        //List<List<Point>> bgArea = new List<List<Point>>();

        // Variables para el Threshold
        int threshold = 140;
        bool autoThreshold = true;

        // Variable para escribir los datos de los diametros en la imagen (no se utiliza por ahora)
        bool txtDiameters = true;

        // Varibles para identificar si el trigger viene del PLC o del Software
        bool triggerPLC = true;
        bool triggerSoftware = false;
        int mode = 0; // 1 - Live, 0 - Frame
        int frameCounter = 0;

        // Variable para actualizar las imagenes si estamos el la imagesTab
        bool updateImages = true;

        // Creamos una lista de colores (no se utilizan por ahora)
        List<Color> colorList = new List<Color>();
        int colorIndex = 0;

        // Control de recursion para el algoritmo de los triangulos
        int maxIteration = 10000;
        int iteration = 0;

        // Parametros para el tamaño de la tortilla (Se van a traer de una base de datos)
        int maxArea = 8000;
        int minArea = 1500;
        double maxDiameter = 88;
        double minDiameter = 72;
        double maxCompactness = 16;
        double maxOvality = 0.5;

        // Resultados de los blobs, los que se colocan en el servidor Modbus
        double maxDiameterAvg = 0;
        double minDiameterAvg = 0;
        double diameterControl = 0;

        // Pagina Avanzado
        int minBlobObjects;
        float alpha;

        // Filtro
        double controlDiameterOld = 0;
        double controlDiameter = 0;

        // Parametros para la calibración (Se van a cargar de un archivo)
        float calibrationTarget = 120;
        string units = "";
        bool calibrating = false;
        double euFactor = 1;
        // Variable para el tipo de grid de la lista gridTypes
        int grid;

        bool processing = false;

        int indexImage = 1;

        // Lista para los strings de los tamaños de la tortilla
        List<string> sizes = new List<string>();

        // Imagen para cargar la imagen tomada por la camara
        public Bitmap originalImage = new Bitmap(1240, 960);
        bool originalImageIsDisposed = true;

        bool roiImagesIsDisposed = false;

        // Directortio para guardar la imagenes para trabajar, es una carpeta tempoal
        string imagesPath = Path.GetTempPath();

        // Crear una lista de blobs
        public List<Blob> Blobs = new List<Blob>();

        // Creamos una lista de cuadrantes
        public List<Quadrant> Quadrants = new List<Quadrant>();

        // Configurar el servidor Modbus TCP
        ModbusServer modbusServer = new ModbusServer();

        Thread thread;
        bool threadSuspended = false;

        List<GridType> gridTypes = new List<GridType>();
        GridType gridType = null;

        // Iniciar el cronómetro
        Stopwatch stopwatch = new Stopwatch();

        static object lockObject = new object();

        Bitmap originalROIImage = new Bitmap(640, 480);
        Mat originalImageCV = new Mat();

        // Hasta aqui las creadas por mi

        // Crear una DataTable para almacenar la información
        DataTable dataTable = new DataTable();

        int Max_Threshold = 255;
        int OffsetLeft = 0;
        int OffsetTop = 0;

        int gridRows = 3;
        int gridCols = 3;

        int operationMode;

        public bool isActivatedProcessData = false; // Variable de estado para el botón tipo toggle

        string csvPath = "";
        string configPath = "";
        // Obtener el directorio de inicio del usuario actual
        string userDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        string archivo = "";

        double targetCalibrationSize = 0;

        Grabber grabber;
        SnapSink sink;
        DeviceInfo deviceInfo;
        PropertyMap propertyMap;
        QueueSink queueSink;

        public Test()
        {
            InitializeComponent();
            initializeElements();
            InitializeDataTable();

            OffsetLeft = originalBox.Location.X;
            OffsetTop = originalBox.Location.Y;

            //----------------Only for Debug, delete on production-----------------
            //MessageBox.Show(settings.frames.ToString() + " frames processed in the last execution");
            settings.frames = 0;
            settings.Save();
            //----------------Only for Debug, delete on production-----------------

            initCamera();

            //// Create a SnapSink. A SnapSink allows grabbing single images (or image sequences) out of a data stream.
            //sink = new SnapSink();
            //// Setup data stream from the video capture device to the sink and start image acquisition.
            //grabber.StreamSetup(sink, StreamSetupOption.AcquisitionStart);

            if (triggerPLC)
            {
                triggerSoftware = false;
                txtTriggerSource.Text = "PLC";
                txtTriggerSource.BackColor = Color.LightGreen;
                virtualTriggerBtn.Enabled = false;
                virtualTriggerBtn.BackColor = Color.DarkGray;

                changeTriggerMode("PLC");

                viewModeBtn.Enabled = false;
                viewModeBtn.BackColor = Color.DarkGray;
                processImageBtn.Enabled = false;
                processImageBtn.BackColor = Color.DarkGray;
                processImageBtn.Text = "PROCESSING";
            }
        }

        private void initCamera()
        {
            Library.Init();

            // Create a grabber object
            grabber = new Grabber();

            // Open the first available video capture device
            deviceInfo = DeviceEnum.Devices.First();
            grabber.DeviceOpen(deviceInfo);

            Console.WriteLine($"Opened device {grabber.DeviceInfo.ModelName}");

            // Set the resolution to 640x480
            grabber.DevicePropertyMap.SetValue(PropId.Width, 1280);
            grabber.DevicePropertyMap.SetValue(PropId.Height, 960);

            propertyMap = grabber.DevicePropertyMap;

            display.Size = new Size(640, 480);

            display.RenderPosition = ic4.DisplayRenderPosition.Custom; //Strecth Image

            display.RenderLeft = 0;
            display.RenderTop = 0;
            display.RenderWidth = 640; 
            display.RenderHeight = 480;

            // Select FrameStart trigger (for cameras that support this)
            try
            {
                propertyMap.SetValue(ic4.PropId.TriggerSelector, "FrameStart");
            }
            catch { }

            if (triggerPLC)
            {
                // Enable trigger mode
                propertyMap.SetValue(ic4.PropId.TriggerMode, "On");
            }
            else
            {
                // Disable trigger mode
                propertyMap.SetValue(ic4.PropId.TriggerMode, "Off");
            }

            // Create a QueueSink to capture all images arriving from the video capture device, specifyin a partial file name
            string path_base = imagesPath + "imagenOrigen.bmp";

            queueSink = new ic4.QueueSink(maxOutputBuffers:1);

            queueSink.FramesQueued += (s, ea) =>
            {
                if (queueSink.TryPopOutputBuffer(out ic4.ImageBuffer buffer))
                {

                    if (triggerPLC || triggerSoftware)
                    {
                        // Generate a file name for the bitmap file
                        var file_name = path_base;

                        // Save the image buffer in the bitmap file
                        try
                        {
                            buffer.SaveAsBitmap(file_name);
                            Console.WriteLine($"Saved image {file_name}");
                        }
                        catch (IC4Exception err)
                        {
                            Console.WriteLine($"Failed to save buffer: {err.Message}");
                        }



                            System.Threading.Thread.Sleep(100);
                            Action safeTrigger = delegate { trigger(); };
                            Invoke(safeTrigger);

                    }
                    else
                    {
                        display.DisplayBuffer(buffer);
                    }

                    buffer.Dispose();
                }
                else
                {
                    Console.WriteLine("Error");
                }
                
            };

            // Start the video stream into the sink
            StartLive();
        }

        private void viewLive(Bitmap image)
        {

        }

        private void Test_Load(object sender, EventArgs e)
        {

        }

        static void PrintDeviceList()
        {
            Console.WriteLine("Enumerating all attached video capture devices...");

            var deviceList = DeviceEnum.Devices;

            if (deviceList.Count == 0)
            {
                Console.WriteLine("No devices found");
            }

            Console.WriteLine($"Found {deviceList.Count} devices:");

            foreach (var deviceInfo in deviceList)
            {
                Console.WriteLine($"\t{FormatDeviceInfo(deviceInfo)}");
            }

            Console.WriteLine();
        }

        static string FormatDeviceInfo(ic4.DeviceInfo deviceInfo)
        {
            return $"Model: {deviceInfo.ModelName} Serial: {deviceInfo.Serial}";
        }

        private void initializeElements()
        {
            //imagesPath = userDir + "\\";

            configurationPage.Enabled = true;
            advancedPage.Enabled = true;

            string actualDIrectory = AppDomain.CurrentDomain.BaseDirectory;
            csvPath = userDir + "\\InspecTorT_db.csv";
            configPath = userDir + "\\InspecTorTConfig";
            archivo = userDir + "\\datos.txt";

            //originalBox.MouseMove += originalBox_MouseMove;
            //processROIBox.MouseMove += processBox_MouseMove;

            //CmbOperationModeSelection.Text = "PLC";
            btnSetPointPLC.BackColor = Color.LightGreen;

            operationMode = 2;
            productsPage.Enabled = false;
            GroupActualTargetSize.Enabled = false;
            GroupSelectGrid.Enabled = false;

            // Suscribir al evento SelectedIndexChanged del TabControl
            mainTabs.SelectedIndexChanged += TabControl2_SelectedIndexChanged;

            processImageBtn.Enabled = false;

            units = settings.Units;
            maxDiameterUnitsTxt.Text = units;
            minDiameterUnitsTxt.Text = units;

            txtAvgDiameterUnits.Text = units;
            txtAvgMaxDiameterUnits.Text = units;
            txtAvgMinDiameterUnits.Text = units;
            txtControlDiameterUnits.Text = units;
            txtEquivalentDiameterUnits.Text = units;

            txtMaxDProductUnits.Text = units;
            txtMinDProductUnits.Text = units;

            txtTriggerSource.BackColor = Color.LightGreen;
            txtViewMode.BackColor = Color.Khaki;

            switch (units)
            {
                case "mm":
                    btnChangeUnitsMm.BackColor = Color.LightGreen;
                    btnChangeUnitsInch.BackColor = Color.Silver;
                    break;
                case "inch":
                    btnChangeUnitsMm.BackColor = Color.Silver;
                    btnChangeUnitsInch.BackColor = Color.LightGreen;
                    break;
            }

            euFactor = settings.EUFactor;
            euFactorTxt.Text = Math.Round(euFactor, 3).ToString();

            formatTxt.Text = settings.Format;

            minBlobObjects = settings.minBlobObjects;
            txtMinBlobObjects.Text = minBlobObjects.ToString();

            alpha = settings.alpha;
            txtAlpha.Text = alpha.ToString();

            // Verificar si el archivo existe
            if (File.Exists(csvPath))
            {
                using (var reader = new StreamReader(new FileStream(csvPath, FileMode.Open), System.Text.Encoding.UTF8))
                using (var csvReader = new CsvReader(reader, CultureInfo.CurrentCulture))
                {
                    var records = csvReader.GetRecords<Product>();
                    foreach (var record in records)
                    {
                        CmbProducts.Items.Add(record.Code);
                    }
                }
            }
            else
            {
                // Encabezados del archivo CSV
                string[] headers = { "Id", "Code", "Name", "MaxD", "MinD", "MaxOvality", "MaxCompacity", "Grid" };

                // Contenido de los registros
                string[][] data = {
                    new string[] { "1","1", "Default", "90", "50", "0.5", "12", "1" },
                    };

                // Escribir los datos en el archivo CSV
                WriteCsvFile(csvPath, headers, data);

                Console.WriteLine("CSV File created succesfully.");

                using (var reader = new StreamReader(new FileStream(csvPath, FileMode.Open), System.Text.Encoding.UTF8))
                using (var csvReader = new CsvReader(reader, CultureInfo.CurrentCulture))
                {
                    var records = csvReader.GetRecords<Product>();
                    foreach (var record in records)
                    {
                        CmbProducts.Items.Add(record.Code);
                        changeProduct(record);
                    }
                }
            }

            // Suscribirse al evento SelectedIndexChanged del ComboBox
            CmbProducts.SelectedIndexChanged += CmbProducts_SelectedIndexChanged;

            modbusServer.Port = 502;
            modbusServer.Listen();
            Console.WriteLine("Modbus Server running...1");

            // Aquí vamos a agregar todos los formatos
            // 3x3
            int[] quadrantsOfinterest = { 1, 2, 3, 4, 5, 6, 7, 8, 9 };
            gridTypes.Add(new GridType(1, (3, 3), quadrantsOfinterest));
            // 5
            quadrantsOfinterest = new int[] { 1, 3, 5, 7, 9 };
            gridTypes.Add(new GridType(2, (3, 3), quadrantsOfinterest));
            // 4x4
            quadrantsOfinterest = new int[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
            gridTypes.Add(new GridType(3, (4, 4), quadrantsOfinterest));
            // 2x2
            quadrantsOfinterest = new int[] { 1, 2, 3, 4 };
            gridTypes.Add(new GridType(4, (2, 2), quadrantsOfinterest));

            grid = settings.GridType;

            // Cargamos el GridType inicial
            foreach (GridType gridT in gridTypes)
            {
                if (gridT.Type == grid)
                {
                    gridType = gridT;
                }
            }

            sizes.Add("Null");
            sizes.Add("Normal");
            sizes.Add("Big");
            sizes.Add("Small");
            sizes.Add("Oval");
            sizes.Add("Oversize");
            sizes.Add("Shape");

            //objeto ROI
            UserROI.Top = settings.ROI_Top;
            UserROI.Left = settings.ROI_Left;
            UserROI.Right = settings.ROI_Right;
            UserROI.Bottom = settings.ROI_Bottom;

            int roiWidth = UserROI.Right - UserROI.Left;
            txtRoiWidth.Text = roiWidth.ToString();

            int roiHeight = UserROI.Bottom - UserROI.Top;
            txtRoiHeight.Text = roiHeight.ToString();

            processROIBox.Image = null;
            originalBox.Image = null;


            maxOvality = settings.maxOvality;
            maxCompactness = settings.maxCompacity;
            maxDiameter = (float)settings.maxDiameter;
            minDiameter = (float)settings.minDiameter;

            Txt_MaxDiameter.Text = Math.Round(maxDiameter * euFactor, 3).ToString();
            Txt_MinDiameter.Text = Math.Round(minDiameter * euFactor, 3).ToString();
            Txt_MaxCompacity.Text = maxCompactness.ToString();
            Txt_MaxOvality.Text = maxOvality.ToString();

            //InitializeInterface();
            Txt_MaxCompacity.KeyPress += Txt_MaxCompacity_KeyPress;
            Txt_MaxOvality.KeyPress += Txt_MaxOvality_KeyPress;

            originalBox.MouseClick += originalBox_MouseClick;
            processROIBox.MouseClick += processBox_MouseClick;

            // Crear un TabControl
            TabControl tabControl1 = new TabControl();
            tabControl1.Location = new Point(10, 10);
            tabControl1.Size = new Size(680, 520);
            this.Controls.Add(tabControl1);

            if (autoThreshold)
            {
                btnAutoThreshold.BackColor = Color.LightGreen;
                btnManualThreshold.BackColor = Color.Silver;
            }
            else
            {
                btnAutoThreshold.BackColor = Color.Silver;
                btnManualThreshold.BackColor = Color.LightGreen;
            }

            originalBox.ImageLocation = imagesPath + "roiDraw.jpg";
            processROIBox.ImageLocation = imagesPath + "final.jpg";
        }

        private void InitializeDataTable()
        {
            dataTable = new DataTable();
            dataTable.Columns.Add("Número de Sector");
            dataTable.Columns.Add("Área");
            dataTable.Columns.Add("Diámetro AI");
            dataTable.Columns.Add("Diámetro Triangulos");
            dataTable.Columns.Add("Diámetro mayor (triangulos)");
            dataTable.Columns.Add("Diámetro menor (triangulos)");
            dataTable.Columns.Add("Compacidad");
            dataTable.Columns.Add("Ovalidad");
        }

        public double Filtro(double k)
        {
            double newK = k * (1 - alpha) + controlDiameterOld * alpha;
            controlDiameterOld = newK;
            return newK;
        }

        private void changeTriggerMode(string modeStr)
        {
            string param = "";
            switch (modeStr)
            {
                case "PLC":
                    param = "Line1";
                    break;
                case "SOFTWARE":
                    param = "Software";
                    break;
            }
            propertyMap.SetValue(ic4.PropId.TriggerSource, param);
        }

        private void ModbusServerIPTxt_KeyPress(object sender, KeyPressEventArgs e)
        {

        }

        // Clase de los productos
        public class Product
        {
            public int Id { get; set; }
            public int Code { get; set; }
            public string Name { get; set; }
            public double MaxD { get; set; }
            public double MinD { get; set; }
            public double MaxOvality { get; set; }
            public double MaxCompacity { get; set; }
            public int Grid { get; set; }

        }


        // Clase para representar el grid
        public class GridType
        {
            public int Type { get; set; }
            public (int, int) Grid { get; set; }
            public int[] QuadrantsOfInterest { get; set; }

            public GridType(int type, (int, int) grid, int[] quadrantsOfInterest)
            {
                Type = type;
                Grid = grid;
                QuadrantsOfInterest = quadrantsOfInterest;
            }

        }

        public class Blob
        {
            // Propiedades de la estructura Blob
            public double Area { get; set; }
            //public List<Point> AreaPoints { get; set; }
            public double Perimetro { get; set; }
            public VectorOfPoint PerimetroPoints { get; set; }
            public double DiametroIA { get; set; }
            public double Diametro { get; set; }
            public Point Centro { get; set; }
            public double DMayor { get; set; }
            public double DMenor { get; set; }
            public double Sector { get; set; }
            public double Compacidad { get; set; }
            public double Ovalidad { get; set; }
            public ushort Size { get; set; }
            //public double CorrectionFactor { get; set; }

            // Constructor de la clase Blob
            public Blob(double area, double perimetro, VectorOfPoint perimetroPoints, double diametro, double diametroIA, Point centro, double dMayor, double dMenor, double sector, double compacidad, ushort size, double ovalidad)
            {
                Area = area;
                //AreaPoints = areaPoints;
                Perimetro = perimetro;
                PerimetroPoints = perimetroPoints;
                Diametro = diametro;
                DiametroIA = diametroIA;
                Centro = centro;
                DMayor = dMayor;
                DMenor = dMenor;
                Sector = sector;
                Compacidad = compacidad;
                Size = size;
                Ovalidad = ovalidad;
                //CorrectionFactor = correctionFactor;
            }
        }

        // Clase para representar un cuadrante L_Q1
        public class Quadrant
        {
            public int Number { get; set; }
            public string ClassName { get; set; }
            public bool Found { get; set; }
            public double DiameterAvg { get; set; }
            public double DiameterMax { get; set; }
            public double DiameterMin { get; set; }
            public double Ratio { get; set; }
            public double Compacity { get; set; }

            public Blob Blob { get; set; }

            public Quadrant(int number, string className, bool found, double diameterAvg, double diameterMax, double diameterMin, double compacity, Blob blob)
            {
                // Inicializar las propiedades según sea necesario
                Number = number;
                ClassName = className;
                Found = found;
                DiameterAvg = diameterAvg;
                DiameterMax = diameterMax;
                DiameterMin = diameterMin;
                Ratio = diameterMax / diameterMin;
                Compacity = compacity;
                Blob = Blob;
            }
        }

        private void trigger()
        {

            maxArea = 50000;
            minArea = 10000;

            try
            {
                if (!processing)
                {

                    bool freeze = freezeFrame;
                    processing = true;

                    //if (!freeze)
                    //{
                    //    disposeImages();
                    //}

                    // Crear un Stopwatch
                    Stopwatch stopwatch = new Stopwatch();

                    // Iniciar el cronómetro
                    stopwatch.Start();

                    if (operationMode == 2)
                    {
                        requestModbusData();
                    }

                    updateTemperatures();

                    switch (mode)
                    {
                        case 0:
                            if (freeze)
                            {
                                preProcessFreezed();
                            }
                            else
                            {
                                preProcess();
                            }
                            break;
                    }


                    if (triggerPLC && !calibrating)
                    {
                        if (freeze)
                        {
                            processFreezed();
                        }
                        else
                        {
                            process();
                        }
                    }

                    if (calibrating)
                    {
                        calibrate();
                    }

                    processing = false;

                    // Detener el cronómetro
                    stopwatch.Stop();
                    timeElapsed.Text = stopwatch.ElapsedMilliseconds.ToString() + " ms";
                    stopwatch.Restart();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message);
            }
        }

        private void disposeImages()
        {
            if (processROIBox.Image != null)
            {
                processROIBox.Image.Dispose();
                processROIBox.Image = null;
                processROIBox.Refresh();
            }
            if (originalBox.Image != null)
            {
                originalBox.Image.Dispose();
                originalBox.Image = null;
                originalBox.Refresh();
            }

            if (!originalImageIsDisposed)
            {
                try
                {
                    originalImage.Dispose();
                }
                catch
                {

                }
            }
        }

        private void processFreezed()
        {
            frameCounter++;
            framesProcessed.Text = frameCounter.ToString();

            //----------------Only for Debug, delete on production-----------------
            settings.frames++;
            settings.Save();
            //----------------Only for Debug, delete on production-----------------

            Mat binarizedImage = new Mat();

            // Se binariza la imagen
            try
            {
                //binarizedImage = binarizeImage(originalImage, 0);
                binarizedImage = binarizeImage(originalImageCV, 0);
                originalImageCV.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Binarization problem");
                Console.WriteLine(ex.Message);
                return;
            }

            // Se extrae el ROI de la imagen binarizada
            Mat roiImage = extractROI(binarizedImage);

            try
            {
                // Procesamos el ROI
                blobProcessFreezed(roiImage, processROIBox);
            }
            catch
            {
                MessageBox.Show("Error en el blob");
            }

            try
            {
                if (Blobs.Count >= (int)(gridType.Grid.Item1 * gridType.Grid.Item2 / 2))
                {
                    setModbusData(true);
                }
                else
                {
                    setModbusData(false);
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(e.ToString());
            }

            originalImage.Dispose();
            originalImageIsDisposed = true;

            processImageBtn.Enabled = false;
        }

        private void blobProcessFreezed(Mat image, PictureBox pictureBox)
        {
            Blobs = new List<Blob>();

            var (contours, centers, areas, perimeters) = FindContoursWithEdgesAndCenters(image);

            // Inicializamos variables
            double avgControlD = 0;
            double avgMaxD = 0;
            double avgMinD = 0;
            double avgD = 0;
            int n = 0;

            List<(int, bool)> drawFlags = new List<(int, bool)>();

            foreach (int k in gridType.QuadrantsOfInterest)
            {
                drawFlags.Add((k, true));
            }

            for (int i = 0; i < areas.Count; i++)
            {
                Point centro = centers[i];

                // Calcular el sector del contorno
                int sector = CalculateSector(centro, image.Width, image.Height, gridType.Grid.Item1, gridType.Grid.Item2) + 1;

                // Verificamos si el sector es uno de los que nos interesa
                if (Array.IndexOf(gridType.QuadrantsOfInterest, sector) != -1)
                {

                    double area = areas[i];
                    double perimeter = perimeters[i];

                    double tempFactor = euFactor;

                    // Este diametro lo vamos a dejar para despues
                    double diametroIA = CalculateDiameterFromArea((int)area);

                    // Calculamos el diametro
                    (double diameterTriangles, double maxDiameter, double minDiameter) = calculateAndDrawDiameterTrianglesAlghoritm(centro, image.ToBitmap(), sector, false);

                    // Sumamos para promediar
                    avgControlD += (diametroIA * tempFactor);
                    avgMaxD += (maxDiameter * tempFactor);
                    avgMinD += (minDiameter * tempFactor);
                    avgD += (diameterTriangles * tempFactor);
                    // Aumentamos el numero de elementos para promediar
                    n++;

                    // Calcular la compacidad
                    double compactness = CalculateCompactness((int)area, perimeter);

                    double ovalidad = calculateOvality(maxDiameter, minDiameter);

                    ushort size = calculateSize(maxDiameter, minDiameter, compactness, ovalidad);

                    Blob blob = new Blob(area, perimeter, contours[i], diameterTriangles, diametroIA, centro, maxDiameter, minDiameter, sector, compactness, size, ovalidad);

                    // Agregamos el elemento a la lista
                    Blobs.Add(blob);

                    foreach (Quadrant quadrant in Quadrants)
                    {
                        if (quadrant.Number == sector)
                        {
                            quadrant.DiameterMax = maxDiameter;
                            quadrant.DiameterMin = minDiameter;
                            quadrant.Compacity = compactness;
                            quadrant.Found = true;
                            quadrant.Blob = blob;

                            for (int l = 0; l < drawFlags.Count; l++)
                            {
                                if (drawFlags[l].Item1 == sector)
                                {
                                    drawFlags[l] = (sector, false);
                                }
                            }

                            break;
                        }
                    }
                }
            }

            // Calculamos el promedio de los diametros
            avgControlD /= n;
            avgMaxD /= n;
            avgMinD /= n;
            avgD /= n;

            //avgControlD = filtro.Aplicar(avgControlD, maxDiameter * euFactor, minDiameter * euFactor);

            maxDiameterAvg = avgMaxD;
            minDiameterAvg = avgMinD;
            diameterControl = avgControlD;
        }

        private void preProcessFreezed()
        {
            processImageBtn.Enabled = true;

            string path = saveImage();
            originalImage = new Bitmap(path);
            originalImageIsDisposed = false;

            Image<Bgr, byte> tempImage = originalImage.ToImage<Bgr, byte>();
            ImageHistogram(originalImage);
            originalImageIsDisposed = true;

            originalImageCV = new Mat();
            originalImageCV = imageCorrection(tempImage);

            originalImageCV.Save(imagesPath + "updatedROI.jpg");

            Quadrants = new List<Quadrant>();

            for (int i = 1; i < 17; i++)
            {
                VectorOfPoint points = new VectorOfPoint();
                Point centro = new Point();
                Blob blb = new Blob(0, 0, points, 0, 0, centro, 0, 0, 0, 0, 0, 0);
                Quadrant qua = new Quadrant(i, "", false, 0, 0, 0, 0, blb);
                Quadrants.Add(qua);
            }
        }

        private void updateTemperatures()
        {
            double deviceTemperature = 0;
            double sensorTemperature = 0;
        }

        private void requestModbusData()
        {
            try
            {
                var registers = modbusServer.holdingRegisters.localArray;

                int numData = 5;
                int startAddress = 250;
                List<float> setPoints = new List<float>();

                for (int i = startAddress; i < (startAddress + (numData * 2)); i += 2)
                {
                    ushort[] registerValue = new ushort[] { (ushort)registers[i], (ushort)registers[i + 1] };                                                                                                          // Combina los dos valores de 16 bits en un solo valor entero de 32 bits
                    int intValue = (registerValue[0] << 16) | registerValue[1];
                    float floatValue = BitConverter.ToSingle(BitConverter.GetBytes(intValue), 0);
                    setPoints.Add(floatValue);
                    // Console.WriteLine(floatValue);
                }

                maxDiameter = setPoints[0] / euFactor;
                settings.maxDiameter = maxDiameter;

                minDiameter = setPoints[1] / euFactor;
                settings.minDiameter = minDiameter;

                maxOvality = setPoints[2];
                settings.maxOvality = (float)maxOvality;

                maxCompactness = setPoints[3];
                settings.maxCompacity = (float)maxCompactness;

                // grid = (int)setPoints[4];
                // settings.GridType = grid;
                // updateGridType(grid);

                updateLabels();

            }
            catch
            {
                Console.WriteLine("Registers could no be read");
            }
        }

        private void updateLabels()
        {
            Txt_MaxDiameter.Text = (maxDiameter * euFactor).ToString();
            Txt_MinDiameter.Text = (minDiameter * euFactor).ToString();
            Txt_MaxOvality.Text = (maxOvality).ToString();
            Txt_MaxCompacity.Text = (maxCompactness).ToString();
        }

        private void preProcess()
        {
            string path = saveImage();
            originalImage = new Bitmap(path);
            originalImageIsDisposed = false;

            // Convertir el objeto Bitmap a una matriz de Emgu CV (Image<Bgr, byte>)
            Image<Bgr, byte> tempImage = originalImage.ToImage<Bgr, byte>();
            ImageHistogram(originalImage);
            originalImageIsDisposed = true;

            originalImageCV = new Mat();
            originalImageCV = imageCorrection(tempImage);

            originalImageCV.Save(imagesPath + "updatedROI.jpg");

            Mat resizedImage = drawROI(originalImageCV.Clone());

            //originalImageCV.Save(imagesPath + "roiDraw.jpg");

            int newWidth = 640;
            int newHeight = 480;

            // Redimensionar la imagen
            CvInvoke.Resize(resizedImage, resizedImage, new Size(newWidth, newHeight), 0, 0, Inter.Linear);

            resizedImage.Save(imagesPath + "roiDraw.jpg");
            resizedImage.Dispose();

            disposeImages();

            originalBox.SizeMode = PictureBoxSizeMode.AutoSize;
            originalBox.Visible = true;
            originalBox.LoadAsync();

            originalBox.BringToFront();
            //processROIBox.SendToBack();

            Quadrants = new List<Quadrant>();

            for (int i = 1; i < 17; i++)
            {
                VectorOfPoint points = new VectorOfPoint();
                Point centro = new Point();
                Blob blb = new Blob(0, 0, points, 0, 0, centro, 0, 0, 0, 0, 0, 0);
                Quadrant qua = new Quadrant(i, "", false, 0, 0, 0, 0, blb);
                Quadrants.Add(qua);
            }

            processImageBtn.Enabled = true;
            processImageBtn.BackColor = Color.Silver;
        }

        private void calibrate()
        {
            // Cambiamos al modo de grid 3x3 para calibrar con la del centro
            updateGridType(1);

            Mat binarizedImage = new Mat();

            // Se binariza la imagen
            try
            {
                //binarizedImage = binarizeImage(originalImage, 0);
                binarizedImage = binarizeImage(originalImageCV, 0);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Binarization problem");
                Console.WriteLine(ex.Message);
                return;
            }

            //// Se extrae el ROI de la imagen binarizada
            Mat roiImage = extractROI(binarizedImage);

            int sectorSel = 5;

            // Se extrae el sector central
            Bitmap centralSector = extractSector(roiImage.ToBitmap(), sectorSel);

            float diametroIA = 0;
            //double diameter = 0;
            //double maxD = 0;
            //double minD = 0;
            bool calibrationValidate = false;

            //centralSector.Save(imagesPath + "centralSector.bmp");

            var (contours, centers, areas, perimeters) = FindContoursWithEdgesAndCenters(roiImage);

            Point centro = new Point();

            for (int i = 0; i < areas.Count; i++)
            {
                int area = (int)areas[i];
                int perimeter = (int)perimeters[i];
                centro = centers[i];

                int sector = CalculateSector(centro, roiImage.Width, roiImage.Height, 3, 3) + 1;

                if (sector == sectorSel)
                {
                    if (itsInCenter(centralSector, centro, 10))
                    {
                        diametroIA = (float)CalculateDiameterFromArea(area);

                        // (diameter, maxD, minD) = calculateAndDrawDiameterTrianglesAlghoritm(centro, roiImage, sector, false);

                        calibrationValidate = true;
                        break;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            // Obtener las coordenadas del centro de la imagen
            int centroX = 1240 / 2;
            int centroY = 960 / 2;

            if (calibrationValidate)
            {
                double tempFactor = targetCalibrationSize / diametroIA; // unit/pixels
                                                                        // Mostrar un MessageBox con un mensaje y botones de opción
                DialogResult result = MessageBox.Show($"A factor of {tempFactor} was obtained. Do you want to continue?", "Warning", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);

                // Verificar la opción seleccionada por el usuario
                if (result == DialogResult.OK)
                {
                    // Si el usuario elige "Sí", continuar con la acción deseada
                    // Agrega aquí el código que deseas ejecutar después de que el usuario confirme
                    euFactor = tempFactor;
                    settings.EUFactor = euFactor;
                    euFactorTxt.Text = Math.Round(euFactor, 3).ToString();
                    maxDiameter = double.Parse(Txt_MaxDiameter.Text) / euFactor;
                    minDiameter = double.Parse(Txt_MinDiameter.Text) / euFactor;
                    settings.maxDiameter = maxDiameter;
                    settings.minDiameter = minDiameter;

                    MessageBox.Show("Calibration Succesful, Factor: " + euFactor);
                }
                else
                {
                    // Si el usuario elige "No", puedes hacer algo o simplemente salir
                    MessageBox.Show("Operation canceled.");
                }
            }
            else
            {
                Console.WriteLine(centro.X + " " + centro.Y);
                MessageBox.Show("Place the calibration target in the middle. Error = X:" + (centro.X + UserROI.Left - centroX) + ", Y:" + (centroY - (centro.Y + UserROI.Top)));
            }

            updateGridType(grid);

            // Liberamos las imagenes
            binarizedImage.Dispose();
            roiImage.Dispose();
            centralSector.Dispose();

            originalImage.Dispose();
            originalImageIsDisposed = true;

            processImageBtn.Enabled = false;

            calibrating = false;
        }

        public Bitmap undistortImage(Bitmap imagen)
        {
            // Bitmap imagen = new Bitmap(originalImage);

            // Dimensiones de la imagen
            int alto = imagen.Height;
            int ancho = imagen.Width;

            double k1 = -1.158e-6;//-1.6105e-6;//-1.158e-6;// -24.4641724;
            double k2 = 1.56e-12;//1.28317e-11;//1.56e-12;//-108.33681;

            double cx = 319;
            double cy = 239;

            // Crear una imagen corregida vacía
            Bitmap imagenCorregida = new Bitmap(ancho, alto);

            // Obtener todos los píxeles de la imagen original de una vez
            Color[,] pixels = new Color[ancho, alto];
            for (int x = 0; x < ancho; x++)
            {
                for (int y = 0; y < alto; y++)
                {
                    pixels[x, y] = imagen.GetPixel(x, y);
                }
            }

            // Procesar cada sección de la imagen en paralelo
            Parallel.For(0, alto, yCorregido =>
            {
                for (int xCorregido = 0; xCorregido < ancho; xCorregido++)
                {

                    double xNormalizado = (xCorregido - cx);
                    double yNormalizado = (yCorregido - cy);

                    double radio = Math.Sqrt(xNormalizado * xNormalizado + yNormalizado * yNormalizado);

                    double factorCorreccionRadial = 1 + k1 * Math.Pow(radio, 2) + k2 * Math.Pow(radio, 4);

                    double xNormalizadoCorregido = xNormalizado * factorCorreccionRadial;
                    double yNormalizadoCorregido = yNormalizado * factorCorreccionRadial;

                    var xCorregidoFinal = (xNormalizadoCorregido + cx);
                    var yCorregidoFinal = (yNormalizadoCorregido + cy);

                    // Procesar cada píxel de la sección dentro de un bloqueo
                    lock (lockObject)
                    {

                        if (0 <= xCorregidoFinal && xCorregidoFinal < ancho - 1 && 0 <= yCorregidoFinal && yCorregidoFinal < alto - 1)
                        {
                            int x0 = (int)xCorregidoFinal;
                            int y0 = (int)yCorregidoFinal;
                            int x1 = x0 + 1;
                            int y1 = y0 + 1;

                            double dx = xCorregidoFinal - x0;
                            double dy = yCorregidoFinal - y0;

                            Color c00 = pixels[x0, y0];
                            Color c10 = pixels[x1, y0];
                            Color c01 = pixels[x0, y1];
                            Color c11 = pixels[x1, y1];

                            double dr = (1 - dx) * (1 - dy) * c00.R + dx * (1 - dy) * c10.R + (1 - dx) * dy * c01.R + dx * dy * c11.R;
                            double dg = (1 - dx) * (1 - dy) * c00.G + dx * (1 - dy) * c10.G + (1 - dx) * dy * c01.G + dx * dy * c11.G;
                            double db = (1 - dx) * (1 - dy) * c00.B + dx * (1 - dy) * c10.B + (1 - dx) * dy * c01.B + dx * dy * c11.B;

                            Color valorPixel = Color.FromArgb((int)dr, (int)dg, (int)db);
                            imagenCorregida.SetPixel(xCorregido, yCorregido, valorPixel);
                        }
                        else
                        {
                            // Si las coordenadas están fuera de la imagen, simplemente copiar el valor del píxel original
                            imagenCorregida.SetPixel(xCorregido, yCorregido, Color.Black);//pixels[(int)xCorregido, (int)yCorregido]);//pixels[(int)xNormalizadoCorregido, (int)yNormalizadoCorregido]);
                        }
                    }
                }
            });

            imagen.Dispose();

            imagenCorregida.Save(imagesPath + "imagenCorregida.bmp");

            return imagenCorregida;
        }


        // Interpolación bilineal entre cuatro píxeles
        static Color InterpolarBilineal(Color c00, Color c10, Color c01, Color c11, double dx, double dy)
        {
            double dr = (1 - dx) * (1 - dy) * c00.R + dx * (1 - dy) * c10.R + (1 - dx) * dy * c01.R + dx * dy * c11.R;
            double dg = (1 - dx) * (1 - dy) * c00.G + dx * (1 - dy) * c10.G + (1 - dx) * dy * c01.G + dx * dy * c11.G;
            double db = (1 - dx) * (1 - dy) * c00.B + dx * (1 - dy) * c10.B + (1 - dx) * dy * c01.B + dx * dy * c11.B;
            return Color.FromArgb((int)dr, (int)dg, (int)db);
        }

        public Mat imageCorrection(Image<Bgr, byte> image)
        {
            // Declarar el vector de coeficientes de distorsión manualmente
            Matrix<double> distCoeffs = new Matrix<double>(1, 5); // 5 coeficientes de distorsión

            double k1 = -1.8568e-7;//-21.4641724 - 6;
            double k2 = -3.4286e-13 + 3.75e-13;//1391.66319 - 700;
            double p1 = 0;
            double p2 = 0;
            double k3 = 0;

            // Asignar los valores de los coeficientes de distorsión
            distCoeffs[0, 0] = k1; // k1
            distCoeffs[0, 1] = k2; // k2
            distCoeffs[0, 2] = p1; // p1
            distCoeffs[0, 3] = p2; // p2
            distCoeffs[0, 4] = k3; // k3

            Matrix<double> cameraMatrix = new Matrix<double>(3, 3);

            double fx = 1;//4728.60;
            double fy = 1;// 4623.52;
            double cx = 640;
            double cy = 480;

            cameraMatrix[0, 0] = fx;
            cameraMatrix[0, 2] = cx;
            cameraMatrix[1, 1] = fy;
            cameraMatrix[1, 2] = cy;
            cameraMatrix[2, 2] = 1;

            // Corregir la distorsión en la imagen
            Mat undistortedImage = new Mat();
            CvInvoke.Undistort(image, undistortedImage, cameraMatrix, distCoeffs);
            image.Dispose();

            return undistortedImage;
        }

        private bool itsInCenter(Bitmap image, Point center, int margin)
        {
            // Obtener las coordenadas del centro de la imagen
            int centroX = 1280/2;
            int centroY = 960/2;

            // Verificar si el punto está dentro del área central definida por el margen de error
            if (Math.Abs(center.X + UserROI.Left - centroX) <= margin && Math.Abs(center.Y + UserROI.Top - centroY) <= margin)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        private Bitmap extractSector(Bitmap binarizedImage, int sectorSel)
        {
            // Calcular el tamaño de cada sector
            int anchoSector = binarizedImage.Width / gridCols;
            int altoSector = binarizedImage.Height / gridRows;

            // Calcular las coordenadas del ROI del sector central
            int x = 1 * anchoSector;
            int y = 1 * altoSector;
            int anchoROI = anchoSector;
            int altoROI = altoSector;

            // Crear y devolver el rectángulo del ROI
            Rectangle sectorRoi = new Rectangle(x, y, anchoROI, altoROI);

            Bitmap roiImage = binarizedImage.Clone(sectorRoi, binarizedImage.PixelFormat);


            return roiImage;
        }

        private int calculateCentralSector()
        {
            // Calcular el índice del sector central
            int indiceFilaCentral = gridRows / 2;
            int indiceColumnaCentral = gridCols / 2;

            // Calcular el número del sector central
            int numeroSectorCentral = indiceFilaCentral * gridCols + indiceColumnaCentral + 1;

            return numeroSectorCentral;
        }

        private void process()
        {
            frameCounter++;
            framesProcessed.Text = frameCounter.ToString();

            //----------------Only for Debug, delete on production-----------------
            settings.frames++;
            settings.Save();
            //----------------Only for Debug, delete on production-----------------

            //originalBox.Visible = true;
            processROIBox.Visible = true; // Mostrar el PictureBox ROI
            Mat binarizedImage = new Mat();

            try
            {
                CvInvoke.GaussianBlur(originalImageCV, originalImageCV, new Size(15, 15), 1.5);

                //CvInvoke.Imshow("Blur", originalImageCV);

                CvInvoke.CvtColor(originalImageCV, originalImageCV, ColorConversion.Bgr2Gray);

                CvInvoke.EqualizeHist(originalImageCV, originalImageCV);

                CvInvoke.CvtColor(originalImageCV, originalImageCV, ColorConversion.Gray2Bgr);
                //CvInvoke.Imshow("Blur + Equalize", originalImageCV);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }


            // Se binariza la imagen
            try
            {
                //binarizedImage = binarizeImage(originalImage, 0);
                binarizedImage = binarizeImage(originalImageCV, 0);
                originalImageCV.Dispose();
                originalImageCV = null;
            }
            catch
            {
                Console.WriteLine("Binarization problem");
                return;
            }

            // Se extrae el ROI de la imagen binarizada
            Mat roiImage = extractROI(binarizedImage);

            // Colocamos el picturebox del ROI
            SetPictureBoxPositionAndSize(processROIBox, imagePage);

            try
            {
                // Procesamos el ROI
                blobProces(roiImage, processROIBox);
                processROIBox.LoadAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error en el blob");
                Console.WriteLine(ex.Message);
            }

            try
            {
                if (Blobs.Count >= minBlobObjects)
                {
                    setModbusData(true);
                }
                else
                {
                    setModbusData(false);
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(e.ToString());
            }

            processImageBtn.Enabled = false;
            processImageBtn.BackColor = Color.DarkGray;
        }

        private void setModbusData(bool quadrantsOK)
        {
            // Frame Counter
            // Número flotante que deseas publicar
            float floatValue = (float)frameCounter;
            // Convertir el número flotante a bytes
            byte[] floatBytes = BitConverter.GetBytes(floatValue);
            // Escribir los bytes en dos registros de 16 bits (dos palabras)
            ushort register1 = BitConverter.ToUInt16(floatBytes, 0);
            ushort register2 = BitConverter.ToUInt16(floatBytes, 2);
            modbusServer.holdingRegisters[1] = (short)register1;
            modbusServer.holdingRegisters[2] = (short)register2;

            // Mode
            floatValue = (float)operationMode;
            // Convertir el número flotante a bytes
            floatBytes = BitConverter.GetBytes(floatValue);
            // Escribir los bytes en dos registros de 16 bits (dos palabras)
            register1 = BitConverter.ToUInt16(floatBytes, 0);
            register2 = BitConverter.ToUInt16(floatBytes, 2);
            modbusServer.holdingRegisters[3] = (short)register1;
            modbusServer.holdingRegisters[4] = (short)register2;

            // GridType
            floatValue = (float)grid;
            // Convertir el número flotante a bytes
            floatBytes = BitConverter.GetBytes(floatValue);
            // Escribir los bytes en dos registros de 16 bits (dos palabras)
            register1 = BitConverter.ToUInt16(floatBytes, 0);
            register2 = BitConverter.ToUInt16(floatBytes, 2);
            modbusServer.holdingRegisters[5] = (short)register1;
            modbusServer.holdingRegisters[6] = (short)register2;

            // Max Diameter SP
            floatValue = (float)(maxDiameter * euFactor);
            // Convertir el número flotante a bytes
            floatBytes = BitConverter.GetBytes(floatValue);
            // Escribir los bytes en dos registros de 16 bits (dos palabras)
            register1 = BitConverter.ToUInt16(floatBytes, 0);
            register2 = BitConverter.ToUInt16(floatBytes, 2);
            modbusServer.holdingRegisters[7] = (short)register1;
            modbusServer.holdingRegisters[8] = (short)register2;

            // Min Diameter SP
            floatValue = (float)(minDiameter * euFactor);
            floatBytes = BitConverter.GetBytes(floatValue);
            // Escribir los bytes en dos registros de 16 bits (dos palabras)
            register1 = BitConverter.ToUInt16(floatBytes, 0);
            register2 = BitConverter.ToUInt16(floatBytes, 2);
            modbusServer.holdingRegisters[9] = (short)register1;
            modbusServer.holdingRegisters[10] = (short)register2;

            // Ovality SP
            // Número flotante que deseas publicar
            floatValue = (float)maxOvality;
            // Convertir el número flotante a bytes
            floatBytes = BitConverter.GetBytes(floatValue);
            // Escribir los bytes en dos registros de 16 bits (dos palabras)
            register1 = BitConverter.ToUInt16(floatBytes, 0);
            register2 = BitConverter.ToUInt16(floatBytes, 2);
            modbusServer.holdingRegisters[11] = (short)register1;
            modbusServer.holdingRegisters[12] = (short)register2;

            // Compacity SP
            // Número flotante que deseas publicar
            floatValue = (float)maxCompactness;
            // Convertir el número flotante a bytes
            floatBytes = BitConverter.GetBytes(floatValue);
            // Escribir los bytes en dos registros de 16 bits (dos palabras)
            register1 = BitConverter.ToUInt16(floatBytes, 0);
            register2 = BitConverter.ToUInt16(floatBytes, 2);
            modbusServer.holdingRegisters[13] = (short)register1;
            modbusServer.holdingRegisters[14] = (short)register2;

            if (quadrantsOK)
            {

                // Número flotante que deseas publicar
                floatValue = (float)diameterControl;
                // Convertir el número flotante a bytes
                floatBytes = BitConverter.GetBytes(floatValue);
                // Escribir los bytes en dos registros de 16 bits (dos palabras)
                register1 = BitConverter.ToUInt16(floatBytes, 0);
                register2 = BitConverter.ToUInt16(floatBytes, 2);
                modbusServer.holdingRegisters[15] = (short)register1;
                modbusServer.holdingRegisters[16] = (short)register2;

                // Número flotante que deseas publicar
                floatValue = (float)minDiameterAvg;
                // Convertir el número flotante a bytes
                floatBytes = BitConverter.GetBytes(floatValue);
                // Escribir los bytes en dos registros de 16 bits (dos palabras)
                register1 = BitConverter.ToUInt16(floatBytes, 0);
                register2 = BitConverter.ToUInt16(floatBytes, 2);
                modbusServer.holdingRegisters[17] = (short)register1;
                modbusServer.holdingRegisters[18] = (short)register2;

                // Número flotante que deseas publicar
                floatValue = (float)maxDiameterAvg;
                // Convertir el número flotante a bytes
                floatBytes = BitConverter.GetBytes(floatValue);
                // Escribir los bytes en dos registros de 16 bits (dos palabras)
                register1 = BitConverter.ToUInt16(floatBytes, 0);
                register2 = BitConverter.ToUInt16(floatBytes, 2);
                modbusServer.holdingRegisters[19] = (short)register1;
                modbusServer.holdingRegisters[20] = (short)register2;

                int offset = 14;
                int firtsRegister = 0;
                foreach (Quadrant q in Quadrants)
                {
                    firtsRegister = offset * q.Number + 11;
                    if (gridType.QuadrantsOfInterest.Contains(q.Number))
                    {
                        if (q.Found)
                        {
                            // Class
                            // Número flotante que deseas publicar
                            floatValue = (float)q.Blob.Size;
                            // Convertir el número flotante a bytes
                            floatBytes = BitConverter.GetBytes(floatValue);
                            // Escribir los bytes en dos registros de 16 bits (dos palabras)
                            register1 = BitConverter.ToUInt16(floatBytes, 0);
                            register2 = BitConverter.ToUInt16(floatBytes, 2);
                            modbusServer.holdingRegisters[firtsRegister] = (short)register1;
                            modbusServer.holdingRegisters[firtsRegister + 1] = (short)register2;

                            // Found
                            // Número flotante que deseas publicar
                            floatValue = q.Found ? 1.0f : 0.0f;
                            // Convertir el número flotante a bytes
                            floatBytes = BitConverter.GetBytes(floatValue);
                            // Escribir los bytes en dos registros de 16 bits (dos palabras)
                            register1 = BitConverter.ToUInt16(floatBytes, 0);
                            register2 = BitConverter.ToUInt16(floatBytes, 2);
                            modbusServer.holdingRegisters[firtsRegister + 2] = (short)register1;
                            modbusServer.holdingRegisters[firtsRegister + 3] = (short)register2;

                            // Diameter
                            // Número flotante que deseas publicar
                            floatValue = (float)q.Blob.DiametroIA;
                            // Convertir el número flotante a bytes
                            floatBytes = BitConverter.GetBytes(floatValue);
                            // Escribir los bytes en dos registros de 16 bits (dos palabras)
                            register1 = BitConverter.ToUInt16(floatBytes, 0);
                            register2 = BitConverter.ToUInt16(floatBytes, 2);
                            modbusServer.holdingRegisters[firtsRegister + 4] = (short)register1;
                            modbusServer.holdingRegisters[firtsRegister + 5] = (short)register2;

                            // Max Diameter
                            // Número flotante que deseas publicar
                            floatValue = (float)q.Blob.DMayor;
                            // Convertir el número flotante a bytes
                            floatBytes = BitConverter.GetBytes(floatValue);
                            // Escribir los bytes en dos registros de 16 bits (dos palabras)
                            register1 = BitConverter.ToUInt16(floatBytes, 0);
                            register2 = BitConverter.ToUInt16(floatBytes, 2);
                            modbusServer.holdingRegisters[firtsRegister + 6] = (short)register1;
                            modbusServer.holdingRegisters[firtsRegister + 7] = (short)register2;

                            // Min Diameter
                            // Número flotante que deseas publicar
                            floatValue = (float)q.Blob.DMenor;
                            // Convertir el número flotante a bytes
                            floatBytes = BitConverter.GetBytes(floatValue);
                            // Escribir los bytes en dos registros de 16 bits (dos palabras)
                            register1 = BitConverter.ToUInt16(floatBytes, 0);
                            register2 = BitConverter.ToUInt16(floatBytes, 2);
                            modbusServer.holdingRegisters[firtsRegister + 8] = (short)register1;
                            modbusServer.holdingRegisters[firtsRegister + 9] = (short)register2;

                            // Ratio
                            // Número flotante que deseas publicar
                            floatValue = (float)(q.Blob.DMayor / q.Blob.DMenor);
                            // Convertir el número flotante a bytes
                            floatBytes = BitConverter.GetBytes(floatValue);
                            // Escribir los bytes en dos registros de 16 bits (dos palabras)
                            register1 = BitConverter.ToUInt16(floatBytes, 0);
                            register2 = BitConverter.ToUInt16(floatBytes, 2);
                            modbusServer.holdingRegisters[firtsRegister + 10] = (short)register1;
                            modbusServer.holdingRegisters[firtsRegister + 11] = (short)register2;

                            // Compacity
                            // Número flotante que deseas publicar
                            floatValue = (float)q.Blob.Compacidad;
                            // Convertir el número flotante a bytes
                            floatBytes = BitConverter.GetBytes(floatValue);
                            // Escribir los bytes en dos registros de 16 bits (dos palabras)
                            register1 = BitConverter.ToUInt16(floatBytes, 0);
                            register2 = BitConverter.ToUInt16(floatBytes, 2);
                            modbusServer.holdingRegisters[firtsRegister + 12] = (short)register1;
                            modbusServer.holdingRegisters[firtsRegister + 13] = (short)register2;
                        }
                        else
                        {
                            // Class
                            // Número flotante que deseas publicar
                            floatValue = 0.0f;
                            // Convertir el número flotante a bytes
                            floatBytes = BitConverter.GetBytes(floatValue);
                            // Escribir los bytes en dos registros de 16 bits (dos palabras)
                            register1 = BitConverter.ToUInt16(floatBytes, 0);
                            register2 = BitConverter.ToUInt16(floatBytes, 2);
                            modbusServer.holdingRegisters[firtsRegister] = (short)register1;
                            modbusServer.holdingRegisters[firtsRegister + 1] = (short)register2;

                            // Found
                            // Número flotante que deseas publicar
                            floatValue = 0.0f;
                            // Convertir el número flotante a bytes
                            floatBytes = BitConverter.GetBytes(floatValue);
                            // Escribir los bytes en dos registros de 16 bits (dos palabras)
                            register1 = BitConverter.ToUInt16(floatBytes, 0);
                            register2 = BitConverter.ToUInt16(floatBytes, 2);
                            modbusServer.holdingRegisters[firtsRegister + 2] = (short)register1;
                            modbusServer.holdingRegisters[firtsRegister + 3] = (short)register2;

                            // Diameter
                            // Número flotante que deseas publicar
                            floatValue = 0.0f;
                            // Convertir el número flotante a bytes
                            floatBytes = BitConverter.GetBytes(floatValue);
                            // Escribir los bytes en dos registros de 16 bits (dos palabras)
                            register1 = BitConverter.ToUInt16(floatBytes, 0);
                            register2 = BitConverter.ToUInt16(floatBytes, 2);
                            modbusServer.holdingRegisters[firtsRegister + 4] = (short)register1;
                            modbusServer.holdingRegisters[firtsRegister + 5] = (short)register2;

                            // Max Diameter
                            // Número flotante que deseas publicar
                            floatValue = 0.0f;
                            // Convertir el número flotante a bytes
                            floatBytes = BitConverter.GetBytes(floatValue);
                            // Escribir los bytes en dos registros de 16 bits (dos palabras)
                            register1 = BitConverter.ToUInt16(floatBytes, 0);
                            register2 = BitConverter.ToUInt16(floatBytes, 2);
                            modbusServer.holdingRegisters[firtsRegister + 6] = (short)register1;
                            modbusServer.holdingRegisters[firtsRegister + 7] = (short)register2;

                            // Min Diameter
                            // Número flotante que deseas publicar
                            floatValue = 0.0f;
                            // Convertir el número flotante a bytes
                            floatBytes = BitConverter.GetBytes(floatValue);
                            // Escribir los bytes en dos registros de 16 bits (dos palabras)
                            register1 = BitConverter.ToUInt16(floatBytes, 0);
                            register2 = BitConverter.ToUInt16(floatBytes, 2);
                            modbusServer.holdingRegisters[firtsRegister + 8] = (short)register1;
                            modbusServer.holdingRegisters[firtsRegister + 9] = (short)register2;

                            // Ratio
                            // Número flotante que deseas publicar
                            floatValue = 0.0f;
                            // Convertir el número flotante a bytes
                            floatBytes = BitConverter.GetBytes(floatValue);
                            // Escribir los bytes en dos registros de 16 bits (dos palabras)
                            register1 = BitConverter.ToUInt16(floatBytes, 0);
                            register2 = BitConverter.ToUInt16(floatBytes, 2);
                            modbusServer.holdingRegisters[firtsRegister + 10] = (short)register1;
                            modbusServer.holdingRegisters[firtsRegister + 11] = (short)register2;

                            // Compacity
                            // Número flotante que deseas publicar
                            floatValue = 0.0f;
                            // Convertir el número flotante a bytes
                            floatBytes = BitConverter.GetBytes(floatValue);
                            // Escribir los bytes en dos registros de 16 bits (dos palabras)
                            register1 = BitConverter.ToUInt16(floatBytes, 0);
                            register2 = BitConverter.ToUInt16(floatBytes, 2);
                            modbusServer.holdingRegisters[firtsRegister + 12] = (short)register1;
                            modbusServer.holdingRegisters[firtsRegister + 13] = (short)register2;
                        }
                    }
                    else
                    {
                        // Class
                        // Número flotante que deseas publicar
                        floatValue = (float)0.0;
                        // Convertir el número flotante a bytes
                        floatBytes = BitConverter.GetBytes(floatValue);
                        // Escribir los bytes en dos registros de 16 bits (dos palabras)
                        register1 = BitConverter.ToUInt16(floatBytes, 0);
                        register2 = BitConverter.ToUInt16(floatBytes, 2);
                        modbusServer.holdingRegisters[firtsRegister] = (short)register1;
                        modbusServer.holdingRegisters[firtsRegister + 1] = (short)register2;

                        // Found
                        // Número flotante que deseas publicar
                        floatValue = 0.0f;
                        // Convertir el número flotante a bytes
                        floatBytes = BitConverter.GetBytes(floatValue);
                        // Escribir los bytes en dos registros de 16 bits (dos palabras)
                        register1 = BitConverter.ToUInt16(floatBytes, 0);
                        register2 = BitConverter.ToUInt16(floatBytes, 2);
                        modbusServer.holdingRegisters[firtsRegister + 2] = (short)register1;
                        modbusServer.holdingRegisters[firtsRegister + 3] = (short)register2;

                        // Diameter
                        // Número flotante que deseas publicar
                        floatValue = 0.0f;
                        // Convertir el número flotante a bytes
                        floatBytes = BitConverter.GetBytes(floatValue);
                        // Escribir los bytes en dos registros de 16 bits (dos palabras)
                        register1 = BitConverter.ToUInt16(floatBytes, 0);
                        register2 = BitConverter.ToUInt16(floatBytes, 2);
                        modbusServer.holdingRegisters[firtsRegister + 4] = (short)register1;
                        modbusServer.holdingRegisters[firtsRegister + 5] = (short)register2;

                        // Max Diameter
                        // Número flotante que deseas publicar
                        floatValue = 0.0f;
                        // Convertir el número flotante a bytes
                        floatBytes = BitConverter.GetBytes(floatValue);
                        // Escribir los bytes en dos registros de 16 bits (dos palabras)
                        register1 = BitConverter.ToUInt16(floatBytes, 0);
                        register2 = BitConverter.ToUInt16(floatBytes, 2);
                        modbusServer.holdingRegisters[firtsRegister + 6] = (short)register1;
                        modbusServer.holdingRegisters[firtsRegister + 7] = (short)register2;

                        // Min Diameter
                        // Número flotante que deseas publicar
                        floatValue = 0.0f;
                        // Convertir el número flotante a bytes
                        floatBytes = BitConverter.GetBytes(floatValue);
                        // Escribir los bytes en dos registros de 16 bits (dos palabras)
                        register1 = BitConverter.ToUInt16(floatBytes, 0);
                        register2 = BitConverter.ToUInt16(floatBytes, 2);
                        modbusServer.holdingRegisters[firtsRegister + 8] = (short)register1;
                        modbusServer.holdingRegisters[firtsRegister + 9] = (short)register2;

                        // Ratio
                        // Número flotante que deseas publicar
                        floatValue = 0.0f;
                        // Convertir el número flotante a bytes
                        floatBytes = BitConverter.GetBytes(floatValue);
                        // Escribir los bytes en dos registros de 16 bits (dos palabras)
                        register1 = BitConverter.ToUInt16(floatBytes, 0);
                        register2 = BitConverter.ToUInt16(floatBytes, 2);
                        modbusServer.holdingRegisters[firtsRegister + 10] = (short)register1;
                        modbusServer.holdingRegisters[firtsRegister + 11] = (short)register2;

                        // Compacity
                        // Número flotante que deseas publicar
                        floatValue = 0.0f;
                        // Convertir el número flotante a bytes
                        floatBytes = BitConverter.GetBytes(floatValue);
                        // Escribir los bytes en dos registros de 16 bits (dos palabras)
                        register1 = BitConverter.ToUInt16(floatBytes, 0);
                        register2 = BitConverter.ToUInt16(floatBytes, 2);
                        modbusServer.holdingRegisters[firtsRegister + 12] = (short)register1;
                        modbusServer.holdingRegisters[firtsRegister + 13] = (short)register2;
                    }
                }
            }
        }

        private Mat extractROI(Mat image)
        {
            
            // Extraer la región del ROI
            // Definir las coordenadas del ROI (rectángulo de interés)
            Rectangle roiRect = new Rectangle(UserROI.Left, UserROI.Top, UserROI.Right - UserROI.Left, UserROI.Bottom - UserROI.Top); // (x, y, ancho, alto)

            // Extraer el ROI de la imagen original
            Mat roiImage = new Mat(image, roiRect);
            image.Dispose();

            return roiImage;
        }

        private Mat drawROI(Mat image)
        {
            //Rectangle rect = new Rectangle(UserROI.Left, UserROI.Top, UserROI.Right - UserROI.Left, UserROI.Bottom - UserROI.Top);
            // Coordenadas y tamaño del rectángulo
            int x = UserROI.Left;
            int y = UserROI.Top;
            int ancho = UserROI.Right - UserROI.Left;
            int alto = UserROI.Bottom - UserROI.Top;

            // Color del rectángulo (en formato BGR)
            MCvScalar color = new MCvScalar(0, 255, 0);

            // Grosor del borde del rectángulo
            int grosor = 2;

            drawHelpLines(image, color, grosor, ancho, alto);

            // Dibujar el rectángulo en la imagen
            CvInvoke.Rectangle(image, new Rectangle(x, y, ancho, alto), color, grosor);

            return image;
        }

        private void drawHelpLines(Mat image, MCvScalar color, int grosor, int ancho, int alto)
        {
            CvInvoke.Line(image,new Point(UserROI.Left, UserROI.Top + ((int)(alto/2))), new Point(UserROI.Right, UserROI.Top + ((int)(alto / 2))),color,grosor);
            CvInvoke.Line(image, new Point(UserROI.Left + ((int)(ancho / 2)), UserROI.Top), new Point(UserROI.Left + ((int)(ancho / 2)), UserROI.Bottom), color, grosor);
        }

        static void WriteCsvFile(string filePath, string[] headers, string[][] data)
        {
            // Crear un StreamWriter para escribir en el archivo CSV
            using (StreamWriter writer = new StreamWriter(filePath))
            {
                // Escribir los encabezados en la primera línea
                writer.WriteLine(string.Join(",", headers));

                // Escribir los datos de cada registro en líneas separadas
                foreach (string[] row in data)
                {
                    writer.WriteLine(string.Join(",", row));
                }
            }
        }

        private void changeProduct(Product record)
        {
            settings.productCode = record.Code;
            CmbProducts.SelectedItem = record.Code;
            Txt_Code.Text = record.Code.ToString();
            Txt_Description.Text = record.Name;
            Txt_MaxD.Text = (record.MaxD * euFactor).ToString();
            Txt_MinD.Text = (record.MinD * euFactor).ToString();
            Txt_Ovality.Text = record.MaxOvality.ToString();
            Txt_Compacity.Text = record.MaxCompacity.ToString();
            string grid = "";
            switch (record.Grid)
            {
                case 1:
                    grid = "3x3";
                    break;
                case 2:
                    grid = "5";
                    break;
                case 3:
                    grid = "4x4";
                    break;
                case 4:
                    grid = "2x2";
                    break;
            }
            CmbGrid.SelectedItem = grid;
        }

        private void CmbProducts_SelectedIndexChanged(object sender, EventArgs e)
        {
            string selectedItem = CmbProducts.SelectedItem.ToString();

            using (var reader = new StreamReader(new FileStream(csvPath, FileMode.Open), System.Text.Encoding.UTF8))
            using (var csvReader = new CsvReader(reader, CultureInfo.CurrentCulture))
            {
                var records = csvReader.GetRecords<Product>();
                //records.Add(new Product { Code = 1, MaxD = 130, MinD = 110, MaxOvality = 0.5, MaxCompacity = 12 });
                //csvWriter.WriteRecords(records);
                foreach (var record in records)
                {
                    if (record.Code == int.Parse(selectedItem))
                    {
                        changeProduct(record);
                    }
                }
            }

        }

        private void updateROI()
        {
            processROIBox.Visible = false;

            Mat originalROIImage = CvInvoke.Imread(imagesPath + "updatedROI.jpg");

            drawROI(originalROIImage);

            CvInvoke.Resize(originalROIImage, originalROIImage, new Size(640, 480), 0, 0, Inter.Linear);

            originalROIImage.Save(imagesPath + "roiDraw.jpg");

            originalBox.LoadAsync();
            originalBox.SizeMode = PictureBoxSizeMode.AutoSize;
            originalBox.Visible = true;

            originalBox.BringToFront();
            processROIBox.SendToBack();
            //m_ImageBox.SendToBack();

            originalROIImage.Dispose();
        }

        private void originalBox_MouseClick(object sender, MouseEventArgs e)
        {
            //// Obtener la posición del ratón dentro del PictureBox
            //Point mousePos = e.Location;

            //// Obtener la imagen del PictureBox
            //Bitmap bitmap = (Bitmap)originalBox.Image;

            //if (bitmap != null && originalBox.ClientRectangle.Contains(mousePos))
            //{
            //    // Obtener el color del píxel en la posición del ratón
            //    Color pixelColor = bitmap.GetPixel(mousePos.X, mousePos.Y);

            //    // Mostrar la información del píxel
            //    PixelDataValue.Text = $"  [ ax= {mousePos.X} y= {mousePos.Y}, Value: {(int)(Math.Round(pixelColor.GetBrightness(), 3) * 255)}]";
            //}
            //bitmap.Dispose();
        }

        private void processBox_MouseClick(object sender, MouseEventArgs e)
        {
            //// Obtener la posición del ratón dentro del PictureBox
            //Point mousePos = e.Location;

            //// Obtener la imagen del PictureBox
            //Bitmap bitmap = (Bitmap)processROIBox.Image;

            //if (bitmap != null && processROIBox.ClientRectangle.Contains(mousePos))
            //{
            //    // Obtener el color del píxel en la posición del ratón
            //    Color pixelColor = bitmap.GetPixel(mousePos.X, mousePos.Y);

            //    // Mostrar la información del píxel
            //    PixelDataValue.Text = $"  [ bx= {mousePos.X + UserROI.Left} y= {mousePos.Y + UserROI.Top}, Value: {(int)(Math.Round(pixelColor.GetBrightness(), 3) * 255)}]";
            //}
            //bitmap.Dispose();
        }

        private void updateUnits(string unitsNew)
        {
            float fact = 0;
            if (units != unitsNew)
            {
                units = unitsNew;
                //targetUnitsTxt.Text = units;
                maxDiameterUnitsTxt.Text = units;
                minDiameterUnitsTxt.Text = units;

                txtAvgDiameterUnits.Text = units;
                txtAvgMaxDiameterUnits.Text = units;
                txtAvgMinDiameterUnits.Text = units;
                txtControlDiameterUnits.Text = units;
                txtEquivalentDiameterUnits.Text = units;

                txtMaxDProductUnits.Text = units;
                txtMinDProductUnits.Text = units;

                switch (unitsNew)
                {
                    // inch/px
                    case "mm":
                        euFactor *= 25.4; //mm/inch
                        fact = 25.4f;
                        break;
                    // mm/px

                    // mm/px
                    case "inch":
                        euFactor *= 0.0393701; // inch/mm
                        fact = 0.0393701f;
                        break;
                        // inch/px
                }

                settings.Units = units;
                euFactorTxt.Text = Math.Round(euFactor, 3).ToString();
                settings.EUFactor = euFactor;

                // Actualizamos los datos de la tabla
                if (dataTable.Rows.Count > 0)
                {
                    dataTable.Clear();
                    foreach (Blob blob in Blobs)
                    {
                        dataTable.Rows.Add(blob.Sector, blob.Area, Math.Round(blob.DiametroIA * euFactor, 3), Math.Round(blob.Diametro * euFactor, 3), Math.Round(blob.DMayor * euFactor, 3), Math.Round(blob.DMenor * euFactor, 3), Math.Round(blob.Compacidad, 3), Math.Round(blob.Ovalidad, 3));
                    }
                }

                if (operationMode == 1)
                {
                    Txt_MaxD.Text = Math.Round(double.Parse(Txt_MaxD.Text) * fact, 3).ToString();
                    Txt_MinD.Text = Math.Round(double.Parse(Txt_MinD.Text) * fact, 3).ToString();

                    //if (unitsNew == "inch")
                    //{
                    //    Txt_MaxD.Text = Math.Round(double.Parse(Txt_MaxD.Text) * fact, 3).ToString();
                    //    Txt_MinD.Text = Math.Round(double.Parse(Txt_MinD.Text) * fact, 3).ToString();
                    //}
                    //else
                    //{
                    //    Txt_MaxD.Text = (int.Parse(Txt_MaxD.Text) * fact).ToString();
                    //    Txt_MinD.Text = (int.Parse(Txt_MinD.Text) * fact).ToString();
                    //}
                }


                double avgDiameter = 0;
                if (Double.TryParse(avg_diameter.Text, out avgDiameter)) ;
                avgDiameter *= fact;
                avg_diameter.Text = Math.Round(avgDiameter, 3).ToString();

                double mxDiameter = 0;
                if (Double.TryParse(Txt_MaxDiameter.Text, out mxDiameter)) ;
                mxDiameter *= fact;
                Txt_MaxDiameter.Text = Math.Round(mxDiameter, 3).ToString();

                double mnDiameter = 0;
                if (Double.TryParse(Txt_MinDiameter.Text, out mnDiameter)) ;
                mnDiameter *= fact;
                Txt_MinDiameter.Text = Math.Round(mnDiameter, 3).ToString();

                double controlDiameter = 0;
                if (Double.TryParse(txtControlDiameter.Text, out controlDiameter)) ;
                controlDiameter *= fact;
                txtControlDiameter.Text = Math.Round(controlDiameter, 3).ToString();

                double avgMinDiameter = 0;
                if (Double.TryParse(txtAvgMinD.Text, out avgMinDiameter)) ;
                avgMinDiameter *= fact;
                txtAvgMinD.Text = Math.Round(avgMinDiameter, 3).ToString();
                
                double avgMaxDiameter = 0;
                if (Double.TryParse(txtAvgMaxD.Text, out avgMaxDiameter)) ;
                avgMaxDiameter *= fact;
                txtAvgMaxD.Text = Math.Round(avgMaxDiameter, 3).ToString();

                double equivalentDiameter = 0;
                if (Double.TryParse(txtEquivalentDiameter.Text, out equivalentDiameter)) ;
                equivalentDiameter *= fact;
                txtEquivalentDiameter.Text = Math.Round(equivalentDiameter, 3).ToString();
            }
        }

        private void Txt_MaxOvality_KeyPress(object sender, KeyPressEventArgs e)
        {
            // Verificar si la tecla presionada es "Enter" (código ASCII 13)
            if (e.KeyChar == (char)Keys.Enter)
            {
                // Intentar convertir el texto del TextBox a un número entero
                if (double.TryParse(Txt_MaxOvality.Text, out maxOvality))
                {
                    // Se ha convertido exitosamente, puedes utilizar la variable threshold aquí
                    MessageBox.Show("Data saved: " + maxOvality, "Guardado", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    settings.maxOvality = (float)maxOvality;
                }
                else
                {
                    // Manejar el caso en que el texto no sea un número válido
                    MessageBox.Show("Use a valid number", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void Txt_MaxCompacity_KeyPress(object sender, KeyPressEventArgs e)
        {
            // Verificar si la tecla presionada es "Enter" (código ASCII 13)
            if (e.KeyChar == (char)Keys.Enter)
            {
                // Intentar convertir el texto del TextBox a un número entero
                if (double.TryParse(Txt_MaxCompacity.Text, out maxCompactness))
                {
                    // Se ha convertido exitosamente, puedes utilizar la variable threshold aquí
                    MessageBox.Show("Data saved: " + maxCompactness, "Guardado", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    settings.maxCompacity = (float)maxCompactness;
                }
                else
                {
                    // Manejar el caso en que el texto no sea un número válido
                    MessageBox.Show("Use a valid number", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void Txt_MinDiameter_KeyPress(object sender, KeyPressEventArgs e)
        {
            // Verificar si la tecla presionada es "Enter" (código ASCII 13)
            if (e.KeyChar == (char)Keys.Enter)
            {
                // Intentar convertir el texto del TextBox a un número entero
                if (double.TryParse(Txt_MinDiameter.Text, out minDiameter))
                {
                    // Se ha convertido exitosamente, puedes utilizar la variable threshold aquí
                    MessageBox.Show("Data saved: " + minDiameter, "Guardado", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    minDiameter = minDiameter / euFactor;
                    settings.minDiameter = minDiameter / euFactor;
                }
                else
                {
                    // Manejar el caso en que el texto no sea un número válido
                    MessageBox.Show("Use a valid number", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void Txt_MaxDiameter_KeyPress(object sender, KeyPressEventArgs e)
        {
            // Verificar si la tecla presionada es "Enter" (código ASCII 13)
            if (e.KeyChar == (char)Keys.Enter)
            {
                // Intentar convertir el texto del TextBox a un número entero
                if (double.TryParse(Txt_MaxDiameter.Text, out maxDiameter))
                {
                    // Se ha convertido exitosamente, puedes utilizar la variable threshold aquí
                    MessageBox.Show("Data saved: " + maxDiameter, "Guardado", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    maxDiameter = maxDiameter / euFactor;
                    settings.maxDiameter = maxDiameter;
                }
                else
                {
                    // Manejar el caso en que el texto no sea un número válido
                    MessageBox.Show("Use a valid number", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        private void TabControl2_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (mainTabs.SelectedTab != imagePage)
            {
                updateImages = false;
            }
            else
            {

                if (freezeFrame)
                {
                    //processROIBox.Image = new Bitmap(imagesPath + "final.bmp");
                    processROIBox.LoadAsync();
                    processROIBox.Refresh();
                }
                else
                {
                    try
                    {
                        if (originalBox.Image != null)
                        {
                            originalBox.Image.Dispose();
                            originalBox.Image = null;
                            //originalBox.Image = new Bitmap(imagesPath + "updatedROI.jpg");
                            originalBox.LoadAsync();
                            originalBox.Refresh();
                        }
                        
                        if (processROIBox.Image != null)
                        {
                            processROIBox.Image.Dispose();
                            processROIBox.Image = null;
                            //processROIBox.Image = new Bitmap(imagesPath + "final.bmp");
                            processROIBox.LoadAsync();
                            processROIBox.Refresh();
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message);
                    }
                }
                //if (!processing)
                //{
                //    try
                //    {
                //        //originalBox.Update();
                //        //processROIBox.Update();
                //    }
                //    catch (Exception ex)
                //    {
                //        Console.WriteLine(ex.Message);
                //    }
                //}

                updateImages = true;
            }
        }

        public string saveImage()
        {
            string imagePath = imagesPath + "imagenOrigen.bmp";

            //// Aqui va a ir el trigger
            //if (mode == 0) Console.WriteLine("Trigger.");

            //try
            //{
            //    // Se guarda la imagen
            //    if(!m_Buffers.Save(imagePath, "-format bmp", -1, 0))
            //    {
            //        Console.WriteLine("Saving Error");
            //    }
            //}
            //catch (SapLibraryException exception)
            //{
            //    Console.WriteLine(exception);
            //}

            // return image;
            return imagePath;
        }

        private int CalculateOtsuThreshold()
        {
            long totalPixels = 0;
            for (int i = 0; i < Histogram.Length; i++)
            {
                totalPixels += Histogram[i];
            }

            double sum = 0;
            for (int i = 0; i < Histogram.Length; i++)
            {
                sum += i * Histogram[i];
            }

            double sumB = 0;
            long wB = 0;
            long wF = 0;

            double varMax = 0;
            int threshold = 0;

            for (int i = 0; i < Histogram.Length; i++)
            {
                wB += Histogram[i];
                if (wB == 0)
                    continue;

                wF = totalPixels - wB;
                if (wF == 0)
                    break;

                sumB += i * Histogram[i];

                double mB = sumB / wB;
                double mF = (sum - sumB) / wF;

                double varBetween = wB * wF * (mB - mF) * (mB - mF);

                if (varBetween > varMax)
                {
                    varMax = varBetween;
                    threshold = i;
                }
            }

            return threshold;
        }

        

        private void blobProces(Mat image, PictureBox pictureBox)
        {
            Blobs = new List<Blob>();

            // Configurar el PictureBox para ajustar automáticamente al tamaño de la imagen
            pictureBox.SizeMode = PictureBoxSizeMode.AutoSize;

            // Mostrar la imagen en el PictureBox
            //pictureBox.Image = image.ToBitmap();

            image.Save(imagesPath + "roi.bmp");

            var (contours, centers, areas, perimeters) = FindContoursWithEdgesAndCenters(image);

            // Limpiamos la tabla
            dataTable.Clear();

            // Inicializamos variables
            double avgMaxD = 0;
            double avgMinD = 0;
            double avgD = 0;
            double avgDIA = 0;
            int n = 0;

            List<(int, bool)> drawFlags = new List<(int, bool)>();

            foreach (int k in gridType.QuadrantsOfInterest)
            {
                drawFlags.Add((k, true));
            }

            for (int i = 0; i < areas.Count; i++)
            {
                Point centro = centers[i];

                // Calcular el sector del contorno
                int sector = CalculateSector(centro, image.Width, image.Height, gridType.Grid.Item1, gridType.Grid.Item2) + 1;

                // Verificamos si el sector es uno de los que nos interesa
                if (Array.IndexOf(gridType.QuadrantsOfInterest, sector) != -1)
                {

                    bool drawFlag = true;

                    foreach ((int, bool) tuple in drawFlags)
                    {
                        if (sector == tuple.Item1)
                        {
                            drawFlag = tuple.Item2;
                        }
                    }

                    int area = (int)areas[i];
                    double perimeter = perimeters[i];

                    double tempFactor = euFactor;

                    // Este diametro lo vamos a dejar para despues
                    double diametroIA = CalculateDiameterFromArea((int)area);

                    // Calculamos el diametro
                    (double diameterTriangles, double maxDiameter, double minDiameter) = calculateAndDrawDiameterTrianglesAlghoritm(centro, image.ToBitmap(), sector, drawFlag);

                    // Calcular la compacidad
                    double compactness = CalculateCompactness((int)area, perimeter);

                    double ovalidad = calculateOvality(maxDiameter, minDiameter);

                    ushort size = calculateSize(maxDiameter, minDiameter, compactness, ovalidad);

                    // Agregamos los datos a la tabla
                    dataTable.Rows.Add(sector, area, Math.Round(diametroIA * tempFactor, 3), Math.Round(diameterTriangles * tempFactor, 3), Math.Round(maxDiameter * tempFactor, 3), Math.Round(minDiameter * tempFactor, 3), Math.Round(compactness, 3), Math.Round(ovalidad, 3));

                    Blob blob = new Blob((double)area, perimeter, contours[i], diameterTriangles, diametroIA, centro, maxDiameter, minDiameter, sector, compactness, size, ovalidad);

                    // Sumamos para promediar
                    avgDIA += (diametroIA);
                    avgMaxD += (maxDiameter * tempFactor);
                    avgMinD += (minDiameter * tempFactor);
                    avgD += (diameterTriangles * tempFactor);
                    // Aumentamos el numero de elementos para promediar
                    n++;

                    // Agregamos el elemento a la lista
                    Blobs.Add(blob);

                    foreach (Quadrant quadrant in Quadrants)
                    {
                        if (quadrant.Number == sector)
                        {
                            quadrant.DiameterMax = maxDiameter;
                            quadrant.DiameterMin = minDiameter;
                            quadrant.Compacity = compactness;
                            quadrant.Found = true;
                            quadrant.Blob = blob;

                            for (int l = 0; l < drawFlags.Count; l++)
                            {
                                if (drawFlags[l].Item1 == sector)
                                {
                                    drawFlags[l] = (sector, false);
                                }
                            }

                            break;
                        }
                    }

                    if (drawFlag)
                    {
                        try
                        {
                            // Dibujamos el centro
                            drawCenter(centro, 2, image);

                            // Dibujamos el sector
                            drawSector(image, sector);

                            // Dibujamos el numero del sector
                            drawSectorNumber(image, centro, sector - 1);

                            drawSize(image, sector, size);
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(ex.Message + "Error dibujando en la imagen");
                        }
                    }
                }
            }

            try
            {
                drawPerimeters(image, contours, 1);

            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message + "Error dibujando perimetros");
            }

            int newWidth = (int)((UserROI.Right - UserROI.Left)*0.5);
            int newHeight = (int)((UserROI.Bottom - UserROI.Top)*0.5);

            // Redimensionar la imagen
            Mat resizedImage = new Mat();
            CvInvoke.Resize(image, resizedImage, new Size(newWidth, newHeight), 0, 0, Inter.Linear);

            resizedImage.Save(imagesPath + "final.jpg");
            resizedImage.Dispose();

            //try
            //{
            //    image.Save(imagesPath + "final.bmp");
            //}
            //catch (Exception e)
            //{
            //    Console.WriteLine(e);
            //}

            // Calculamos el promedio de los diametros
            avgDIA /= n;
            avgMaxD /= n;
            avgMinD /= n;
            avgD /= n;

            if (!double.IsNaN(avgDIA))
            {
                double validateControl = Filtro(avgDIA);
                if (validateControl > maxDiameter * 3)
                {
                    validateControl = maxDiameter * 3;
                }
                else if (validateControl < 0)
                {
                    validateControl = 0;
                }
                controlDiameter = validateControl;
            }
            else
            {
                controlDiameter = Filtro(0);
            }

            // Asignamos el texto del promedio de los diametros
            avg_diameter.Text = Math.Round(avgD, 3).ToString();
            txtAvgMaxD.Text = Math.Round(avgMaxD, 3).ToString();
            txtAvgMinD.Text = Math.Round(avgMinD, 3).ToString();
            txtEquivalentDiameter.Text = Math.Round(avgDIA * euFactor, 3).ToString();
            txtControlDiameter.Text = Math.Round(controlDiameter*euFactor, 3).ToString();

            // Asignar la DataTable al DataGridView
            dataGridView1.DataSource = dataTable;

        }

        private void drawSize(Mat image, int sector, ushort size)
        {
            int sectorWidth = image.Width / gridType.Grid.Item2;
            int sectorHeight = image.Height / gridType.Grid.Item1;

            // Console.WriteLine(sectorWidth);

            // Calcular las coordenadas del sector en el orden deseado
            int textX = ((sector - 1) / gridType.Grid.Item2) * sectorWidth;
            int textY = ((gridType.Grid.Item2 - 1) - ((sector - 1) % gridType.Grid.Item2)) * sectorHeight;

            MCvScalar brush = new MCvScalar();

            switch (size)
            {
                // Normal
                case 1:
                    brush = new MCvScalar(0, 255, 0);
                    break;
                // Big
                case 2:
                    brush = new MCvScalar(0, 165, 255);
                    break;
                // Small
                case 3:
                    brush = new MCvScalar(0, 255, 255);
                    break;
                // Oval
                case 4:
                    brush = new MCvScalar(255, 255, 0);
                    break;
                // Shape
                case 6:
                    brush = new MCvScalar(0, 0, 255);
                    break;
            }

            // Crear el texto a mostrar
            string texto = sizes[size];

            CvInvoke.PutText(image, texto, new Point(textX + 10, textY + 30), FontFace.HersheySimplex, 1, brush, 2);
        }

        public static List<List<Point>> FindBackground(Bitmap binaryImage, int color, int minArea, int maxArea)
        {
            List<List<Point>> contours = new List<List<Point>>();
            var visited = new HashSet<(int, int)>();

            int height = binaryImage.Height;
            int width = binaryImage.Width;

            // Función para verificar si un píxel está dentro de los límites de la imagen
            bool IsWithinBounds(int x, int y)
            {
                return 0 <= x && x < width && 0 <= y && y < height;
            }

            // Encontrar todos los contornos y bordes en la imagen utilizando DFS
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (visited.Contains((x, y)) || binaryImage.GetPixel(x, y).GetBrightness() != color)
                    {
                        continue;
                    }

                    var contour = new List<Point>();
                    var stack = new Stack<(int, int)>();

                    stack.Push((x, y));

                    while (stack.Count > 0)
                    {

                        var (cx, cy) = stack.Pop();
                        if (visited.Contains((cx, cy)) || binaryImage.GetPixel(cx, cy).GetBrightness() != color)
                        {
                            continue;
                        }

                        contour.Add(new Point(cx, cy));
                        visited.Add((cx, cy));

                        foreach (var (dx, dy) in new (int, int)[] { (1, 0), (0, 1), (-1, 0), (0, -1) })
                        {
                            int nx = cx + dx;
                            int ny = cy + dy;
                            if (IsWithinBounds(nx, ny) && !visited.Contains((nx, ny)))
                            {
                                stack.Push((nx, ny));
                            }
                        }

                    }

                    int area = contour.Count;

                    if (area > 0 && area > minArea && area < maxArea)
                    {
                        contours.Add(contour);

                    }

                }
            }

            return (contours);
        }

        private void drawCenter(Point centro, int thickness, Mat image)
        {
            CvInvoke.Circle(image, centro, thickness, new MCvScalar(255, 255, 0));
        }

        // Función para dibujar un punto con un grosor dado
        void drawPerimeters(Mat image, VectorOfVectorOfPoint perimeter, int thickness)
        {
            CvInvoke.DrawContours(image, perimeter, -1, new MCvScalar(255, 255, 0), thickness);
        }

        private double calculateOvality(double maxDiameter, double minDiameter)
        {
            double ovality = Math.Sqrt((1 - (Math.Pow(minDiameter, 2) / Math.Pow(maxDiameter, 2))));
            return ovality;
        }

        private ushort calculateSize(double dMayor, double dMenor, double compacidad, double ovalidad)
        {
            ushort size = 1; // Normal

            if (dMayor > maxDiameter)
            {
                size = 2; // Big
            }
            else if (dMenor < minDiameter)
            {
                size = 3; // Pequeña
            }
            else if (ovalidad > maxOvality)
            {
                size = 4; // Oval
            }
            else if (compacidad > maxCompactness)
            {
                size = 6; // Shape
            }


            return size;
        }

        private Mat binarizeImage(Mat image, int value)
        {

            try
            {
                if (autoThreshold)
                {
                    threshold = CalculateOtsuThreshold();
                    Txt_Threshold.Text = threshold.ToString();
                }
                else
                {
                    threshold = int.Parse(Txt_Threshold.Text);
                }
            }
            catch (FormatException)
            {

            }

            // Aplicar umbralización (binarización)
            Mat imagenBinarizada = new Mat();
            CvInvoke.Threshold(image, imagenBinarizada, threshold, 255, ThresholdType.Binary);
            //image.Dispose();

            // Guardar la imagen binarizada
            imagenBinarizada.Save("imagen_binarizada.jpg");

            return imagenBinarizada;
        }

        private void drawSectorNumber(Mat image, Point center, int sector)
        {
            // Seleccionar la esquina donde se mostrará el número del sector (puedes ajustar según tus necesidades)
            int xOffset = 5;
            int yOffset = 5;

            CvInvoke.PutText(image, (sector + 1).ToString(), new Point(center.X + xOffset, center.Y + yOffset), FontFace.HersheySimplex, 1.2, new MCvScalar(0, 0, 255), 2);

        }

        private int CalculateSector(Point objectCenter, int imageWidth, int imageHeight, int gridRows, int gridCols)
        {

            // Calcular el ancho y alto de cada sector
            int sectorWidth = imageWidth / gridCols;
            int sectorHeight = imageHeight / gridRows;

            // Calcular el sector en el que se encuentra el centro del objeto
            int sectorX = objectCenter.X / sectorWidth;

            // Calcular sectorY de abajo hacia arriba
            int sectorY = gridRows - 1 - (objectCenter.Y / sectorHeight);

            // Calcular el índice del sector en función del número de columnas
            int sectorIndex = sectorX * gridCols + sectorY;

            return sectorIndex;
        }


        private (double, double, double) calculateAndDrawDiameterTrianglesAlghoritm(Point center, Bitmap image, int sector, bool draw = true)
        {

            double diameter, maxDiameter, minDiameter;
            List<Point> listXY = new List<Point>();

            maxDiameter = 0; minDiameter = 0;

            int[] deltaX = { 1, 4, 2, 1, 1, 1, 0, -1, -1, -1, -2, -4, -1, -4, -2, -1, -1, -1, 0, 1, 1, 1, 2, 4 };
            int[] deltaY = { 0, 1, 1, 1, 2, 4, 1, 4, 2, 1, 1, 1, 0, -1, -1, -1, -2, -4, -1, -4, -2, -1, -1, -1 };

            int[] correction = { 0, -2, -1, 0, -1, -2, 0, -2, -1, 0, -1, -2, 0, -2, -1, 0, -1, -2, 0, -2, -1, 0, -1, -2 };

            double avg_diameter = 0;

            int x = center.X;
            int y = center.Y;

            int newX = x;
            int newY = y;

            double[] radialLenght = new double[24];

            for (int i = 0; i < 24; i++)
            {
                iteration = 0;
                Color pixelColor = image.GetPixel(newX, newY);

                while (pixelColor.GetBrightness() != 0)
                {
                    iteration++;

                    newX += deltaX[i];
                    newY += deltaY[i];

                    if (newX >= image.Width || newX < 0)
                    {
                        newX -= deltaX[i];
                    }

                    if (newY >= image.Height || newY < 0)
                    {
                        newY -= deltaY[i];
                    }

                    pixelColor = image.GetPixel(newX, newY);

                    //if (pixelColor.GetBrightness() == 0)
                    //{
                    //    newX -= (int)(deltaX[i] / 2);
                    //    newY -= (int)(deltaY[i] / 2);
                    //    break;
                    //}

                    if (iteration >= maxIteration)
                    {
                        iteration = 0;
                        break;
                    }

                }

                double hipotenusa = Math.Sqrt(Math.Pow(deltaX[i], 2) + Math.Pow(deltaY[i], 2));

                listXY.Add(new Point(newX, newY));

                radialLenght[i] = Math.Sqrt(Math.Pow((x - newX), 2) + Math.Pow((y - newY), 2)) - hipotenusa / 2; //+ correction[i];

                avg_diameter += radialLenght[i];
                newX = x; newY = y;
            }

            diameter = avg_diameter / 12;

            List<double> diameters = new List<double>();

            for (int i = 0; i < 12; i++)
            {
                double diam = radialLenght[i] + radialLenght[i + 12];
                diameters.Add(diam);
            }

            maxDiameter = diameters.Max();
            minDiameter = diameters.Min();


            if (draw)
            {
                int maxIndex = diameters.IndexOf(maxDiameter);
                int minIndex = diameters.IndexOf(minDiameter);

                // CvInvoke.Line(image,new Point(center.X, center.Y), listXY[maxIndex],new MCvScalar(2552,255,0));

                using (Graphics g = Graphics.FromImage(image))
                {
                    Pen pen1 = new Pen(Color.Green, 4);
                    Pen pen2 = new Pen(Color.Red, 4);

                    // Dibujar diámetro máximo
                    g.DrawLine(pen1, new Point(center.X, center.Y), listXY[maxIndex]);
                    g.DrawLine(pen1, new Point(center.X, center.Y), listXY[maxIndex + 12]);

                    // Dibujar diámetro minimo
                    g.DrawLine(pen2, new Point(center.X, center.Y), listXY[minIndex]);
                    g.DrawLine(pen2, new Point(center.X, center.Y), listXY[minIndex + 12]);
                }

            }
            return (diameter, maxDiameter, minDiameter);
        }


        private double CalculateDiameterFromArea(int area)
        {
            const double pi = Math.PI;

            // Calcular el diámetro utilizando la fórmula d = sqrt(4 * Área / pi)
            double diameter = Math.Sqrt(4 * area / pi);

            return diameter;
        }

        private double CalculateCompactness(int area, double perimeter)
        {
            // Lógica para calcular la compacidad
            // Se asume que el área y el perímetro son mayores que cero para evitar divisiones por cero
            double compactness = (perimeter * perimeter) / (double)area;

            return compactness;
        }

        private void drawSector(Mat image, int sector)
        {
            // Calcular el ancho y alto de cada sector
            int sectorWidth = image.Width / gridType.Grid.Item2;
            int sectorHeight = image.Height / gridType.Grid.Item1;

            // Console.WriteLine(sectorWidth);

            // Calcular las coordenadas del sector en el orden deseado
            int sectorX = ((sector - 1) / gridType.Grid.Item2) * sectorWidth;
            int sectorY = ((gridType.Grid.Item2 - 1) - ((sector - 1) % gridType.Grid.Item2)) * sectorHeight;

            // Definir el rectángulo (x, y, ancho, alto)
            Rectangle rect = new Rectangle(sectorX, sectorY, sectorWidth, sectorHeight);

            // Crear un lápiz para el borde del rectángulo (en este caso, color azul y grosor 2)
            CvInvoke.Rectangle(image, rect, new MCvScalar(255, 255, 0),2);
        }

        private void processImageBtn_Click(object sender, EventArgs e)
        {
            process();
        }

        public void startStop()
        {
            //this.StatusLabelInfo.Text = "";
            //this.StatusLabelInfoTrash.Text = "";
            //if (!m_Xfer.Grabbing)
            //{
            //    if (m_Xfer.Grab())
            //    {
            //        UpdateControls();

            //        // viewModeBtn.BackColor = DefaultBackColor; // Restaurar el color de fondo predeterminado
            //        txtViewMode.Text = "LIVE";
            //        txtViewMode.BackColor = Color.LightGreen;
            //        virtualTriggerBtn.Enabled = false;
            //        virtualTriggerBtn.BackColor = Color.DarkGray;
            //        processImageBtn.Enabled = false;
            //        processImageBtn.BackColor = Color.DarkGray;
            //        mode = 1;

            //        if (triggerPLC)
            //        {
            //            txtViewMode.Text = "FRAME";
            //            txtViewMode.BackColor = Color.Khaki;
            //            mode = 0;
            //        }
            //    }
            //}

            //else
            //{
            //    AbortDlg abort = new AbortDlg(m_Xfer);

            //    if (m_Xfer.Freeze())
            //    {
            //        if (abort.ShowDialog() != DialogResult.OK)
            //            m_Xfer.Abort();
            //        UpdateControls();

            //        //viewModeBtn.BackColor = Color.Silver; // Cambiar el color de fondo a gris
            //        txtViewMode.Text = "FRAME"; // Cambiar el texto cuando está desactivado
            //        txtViewMode.BackColor = Color.Khaki;
            //        mode = 0;
            //        processImageBtn.Enabled = true;
            //        processImageBtn.BackColor = Color.Silver;
            //        virtualTriggerBtn.Enabled = true;
            //        virtualTriggerBtn.BackColor = Color.Silver;
            //    }
            //}
        }

        private void triggerModeBtn_Click(object sender, EventArgs e)
        {
            triggerPLC = !triggerPLC;

            if (triggerPLC)
            {
                triggerSoftware = false;
                txtTriggerSource.Text = "PLC";
                txtTriggerSource.BackColor = Color.LightGreen;
                virtualTriggerBtn.Enabled = false;
                virtualTriggerBtn.BackColor = Color.DarkGray;

                changeTriggerMode("PLC");

                startStop();

                viewModeBtn.Enabled = false;
                viewModeBtn.BackColor = Color.DarkGray;
                processImageBtn.Enabled = false;
                processImageBtn.BackColor = Color.DarkGray;
                processImageBtn.Text = "PROCESSING";

            }
            else
            {
                triggerSoftware = true;
                changeTriggerMode("SOFTWARE");

                virtualTriggerBtn.Enabled = true;
                virtualTriggerBtn.BackColor = Color.Silver;

                viewModeBtn.Enabled = true;
                viewModeBtn.BackColor = Color.Silver;
                processImageBtn.Enabled = true;
                processImageBtn.BackColor = Color.Silver;

                txtTriggerSource.Text = "SOFTWARE";
                txtTriggerSource.BackColor = Color.Khaki;
                processImageBtn.Text = "PROCESS FRAME";
                processImageBtn.Enabled = false;
                processImageBtn.BackColor = Color.DarkGray;
            }

        }

        private void Cmd_Trigger_Click1(object sender, EventArgs e)
        {
            throw new NotImplementedException();
        }

        void ImageHistogram(Bitmap originalImage)
        {
            int x, y;
            int BytesPerLine;
            int PixelValue;

            // Obtener BitsPerPixel y PixelPerLine
            int bitsPerPixel = System.Drawing.Image.GetPixelFormatSize(originalImage.PixelFormat);
            int pixelPerLine = originalImage.Width;

            // Initialize Histogram array
            for (int i = 0; i < 256; i++)
            {
                Histogram[i] = 0;
            }

            // Calculate the count of bytes per line using the color format and the
            // pixels per line of the image buffer.
            BytesPerLine = bitsPerPixel / 8 * pixelPerLine - 1;

            // For y = 0 To ImgBuffer.Lines - 1
            // For x = 0 To BytesPerLine
            for (y = UserROI.Top; y <= UserROI.Bottom; y++)
            {
                for (x = UserROI.Left; x <= UserROI.Right; x++)
                {
                    // Assuming 8 bits per pixel (grayscale)
                    Color pixelColor = originalImage.GetPixel(x, y);

                    // Get the grayscale value directly
                    PixelValue = pixelColor.R;

                    Histogram[PixelValue] = Histogram[PixelValue] + 1;
                }
            }
            originalImage.Dispose();
        }

        // Modificar el threshold manualmente
        private void Txt_Threshold_KeyPress(object sender, KeyPressEventArgs e)
        {
            // Verificar si la tecla presionada es "Enter" (código ASCII 13)
            if (e.KeyChar == (char)Keys.Enter)
            {
                // Intentar convertir el texto del TextBox a un número entero
                if (int.TryParse(Txt_Threshold.Text, out threshold))
                {
                    // Se ha convertido exitosamente, puedes utilizar la variable threshold aquí
                    MessageBox.Show("Data saved: " + threshold, "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    // Manejar el caso en que el texto no sea un número válido
                    MessageBox.Show("Use a valid number.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void SetPictureBoxPositionAndSize(PictureBox pictureBox, TabPage tabPage)
        {
            // Calcular el tamaño de la imagen
            int imageWidth =  UserROI.Right - UserROI.Left;
            int imageHeight = UserROI.Bottom - UserROI.Top;

            // Configurar el PictureBox para ajustar automáticamente al tamaño de la imagen
            pictureBox.SizeMode = PictureBoxSizeMode.StretchImage;

            // Establecer el tamaño del PictureBox
            pictureBox.Size = new Size(imageWidth, imageHeight);

            // Ubicar el PictureBox en la posición del ROI
            pictureBox.Location = new Point((int)((UserROI.Left*0.5)+OffsetLeft), (int)((UserROI.Top*0.5) + OffsetTop));

            // Agregar el PictureBox a la misma TabPage que m_ImageBox
            tabPage.Controls.Add(pictureBox);

            originalBox.SendToBack();
            pictureBox.BringToFront();
        }

        private void btnsave_Click(object sender, EventArgs e)
        {
            settings.Save();
            MessageBox.Show("Configuration Saved");
        }

        public void GuardarConfiguracion(string nombreUsuario, string valorConfiguracion)
        {

        }

        private void grid_4_Click(object sender, EventArgs e)
        {
            updateGridType(1, "3x3");
        }

        private void updateGridType(int v, string type = "")
        {
            foreach (GridType gridT in gridTypes)
            {
                if (gridT.Type == v && grid != v)
                {
                    gridType = gridT;
                    if (type != "")
                    {
                        formatTxt.Text = type;
                        settings.Format = type;
                        settings.GridType = v;
                        grid = v;
                        MessageBox.Show("Grid switched to: " + type);
                    }
                    break;
                }
            }
        }

        private void grid_5_Click(object sender, EventArgs e)
        {
            updateGridType(2, "5");
        }

        private void grid_6_Click(object sender, EventArgs e)
        {
            updateGridType(3, "4x4");
        }

        private void grid_9_Click(object sender, EventArgs e)
        {
            updateGridType(4, "2x2");
        }

        private void Cmd_Program_5_Click(object sender, EventArgs e)
        {

        }

        private void Cmd_Program_6_Click(object sender, EventArgs e)
        {

        }

        private void BtnLocalRemote_Click(object sender, EventArgs e)
        {

        }

        private void Chk_Threshold_Mode_CheckedChanged(object sender, EventArgs e)
        {
            autoThreshold = !autoThreshold;
        }

        private void virtualTriggerBtn_Click(object sender, EventArgs e)
        {
            originalBox.BringToFront();
            processROIBox.SendToBack();
            processROIBox.Visible = false;

            // Execute software trigger
            propertyMap.ExecuteCommand(ic4.PropId.TriggerSoftware);

            //bool succes = m_AcqDevice.SetFeatureValue("TriggerSoftware", true);
            //if (succes)
            //{
            //    Console.WriteLine("VirtualTrigger");
            //    processImageBtn.Enabled = true;
            //    processImageBtn.BackColor = Color.Silver;
            //}

        }

        private void diametersTxtCheck_CheckedChanged(object sender, EventArgs e)
        {
            txtDiameters = !txtDiameters;
        }

        private (VectorOfVectorOfPoint, List<Point>, List<double>, List<double>) FindContoursWithEdgesAndCenters(Mat image)
        {
            Mat grayImage = new Mat();
            CvInvoke.CvtColor(image, grayImage, ColorConversion.Bgr2Gray);

            // Encontrar contornos
            VectorOfVectorOfPoint contours = new VectorOfVectorOfPoint();
            VectorOfVectorOfPoint filteredContours = new VectorOfVectorOfPoint();
            Mat jerarquia = new Mat();

            List<Point> centroids = new List<Point>();
            List<double> areas = new List<double>();
            List<double> perimeters = new List<double>();

            CvInvoke.FindContours(grayImage, contours, jerarquia, RetrType.Tree, ChainApproxMethod.ChainApproxSimple);


            // Coloreamos todos los pixeles de fondo
            Array array = jerarquia.GetData();
            for (int i = 0; i < contours.Size; i++)
            {
                // Si el contorno tiene un contorno padre
                int a = (int)array.GetValue(0, i, 3);
                int area = (int)CvInvoke.ContourArea(contours[i]);
                //MessageBox.Show(array.GetValue(0,i,3).ToString());
                if (a != -1 && area > 0 && area < minArea)
                {
                    // Dibujar el contorno interno en verde
                    CvInvoke.DrawContours(image, contours, i, new MCvScalar(255, 255, 0), -1);
                }
            }



            for (int i = 0; i < contours.Size; i++)
            {
                double area = CvInvoke.ContourArea(contours[i]);
                if (area >= minArea && area <= maxArea)
                {
                    double perimeter = CvInvoke.ArcLength(contours[i], true);
                    int indicePrimerHijo = Convert.ToInt32(array.GetValue(0, i, 2));
                    if (indicePrimerHijo != -1)
                    {
                        // El contorno tiene al menos un hijo
                        // Iterar sobre los hijos y hacer lo que necesites
                        int indiceHijoActual = indicePrimerHijo;
                        do
                        {
                            // Acceder al contorno hijo en el vector de contornos
                            VectorOfPoint contornoHijo = contours[indiceHijoActual];

                            area -= CvInvoke.ContourArea(contornoHijo);
                            perimeter += CvInvoke.ArcLength(contornoHijo, true);

                            // Obtener el índice del siguiente hijo del contorno padre actual
                            indiceHijoActual = Convert.ToInt32(array.GetValue(0, indiceHijoActual, 0));

                        } while (indiceHijoActual != -1); // Continuar mientras haya más hijos
                    }

                    areas.Add(area);
                    filteredContours.Push(contours[i]);
                    perimeters.Add(perimeter);

                    var moments = CvInvoke.Moments(contours[i]);
                    if (moments.M00 != 0)
                    {
                        // Calcular centroides
                        float cx = (float)(moments.M10 / moments.M00);
                        float cy = (float)(moments.M01 / moments.M00);
                        centroids.Add(new Point((int)cx, (int)cy));
                    }
                }
            }

            return (filteredContours, centroids, areas, perimeters);
        }


        static Point CalculateCenter(List<Point> contour)
        {
            int sumX = 0;
            int sumY = 0;

            foreach (var point in contour)
            {
                sumX += point.X;
                sumY += point.Y;
            }

            int centerX = sumX / contour.Count;
            int centerY = sumY / contour.Count;

            return new Point(centerX, centerY);
        }

        //Funcion para convertir a la imagen a un formato compatible para dibujar en ella
        public static Bitmap ConvertToCompatibleFormat(Bitmap bitmap)
        {
            // Crear un nuevo Bitmap con el mismo tamaño y formato compatible
            Bitmap compatibleBitmap = new Bitmap(bitmap.Width, bitmap.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            // Copiar los píxeles de la imagen original al nuevo Bitmap
            using (Graphics g = Graphics.FromImage(compatibleBitmap))
            {
                g.DrawImage(bitmap, 0, 0, bitmap.Width, bitmap.Height);
            }

            return compatibleBitmap;
        }

        // CLick en el boton de calibración
        private void calibrateButtom_Click(object sender, EventArgs e)
        {
            if (!triggerPLC && mode == 0)
            {
                calibrating = true;

                using (var inputForm = new InputDlg(units))
                {
                    if (inputForm.ShowDialog() == DialogResult.OK)
                    {
                        targetCalibrationSize = inputForm.targetSize;

                        propertyMap.ExecuteCommand(ic4.PropId.TriggerSoftware);
                        
                        System.Threading.Thread.Sleep(100);

                        trigger();
                    }
                }
            }
            else
            {
                MessageBox.Show("Change the operation mode");
            }

            
            
        }

        private void CmdUpdate_Click(object sender, EventArgs e)
        {
            int selectedItem = int.Parse(CmbProducts.SelectedItem.ToString());

            updateProdut(selectedItem);

            using (var reader = new StreamReader(new FileStream(csvPath, FileMode.Open), System.Text.Encoding.UTF8))
            using (var csvReader = new CsvReader(reader, CultureInfo.CurrentCulture))
            {
                var records = csvReader.GetRecords<Product>();
                CmbProducts.Items.Clear();
                //records.Add(new Product { Code = 1, MaxD = 130, MinD = 110, MaxOvality = 0.5, MaxCompacity = 12 });
                //csvWriter.WriteRecords(records);
                foreach (var record in records)
                {
                    CmbProducts.Items.Add(record.Code);
                }
            }

        }

        private void updateProdut(int selectedItem)
        {
            var records = new List<Product>();

            using (var reader = new StreamReader(new FileStream(csvPath, FileMode.Open), System.Text.Encoding.UTF8))
            using (var csvReader = new CsvReader(reader, CultureInfo.CurrentCulture))
            {
                records = csvReader.GetRecords<Product>().ToList();
                //records.Add(new Product { Code = 1, MaxD = 130, MinD = 110, MaxOvality = 0.5, MaxCompacity = 12 });
                //csvWriter.WriteRecords(records);
                for (int i = 0; i < records.Count(); i++)
                {
                    if (records[i].Code == selectedItem)
                    {
                        int grid = 0;

                        switch (CmbGrid.SelectedItem.ToString())
                        {
                            case "3x3":
                                grid = 1;
                                break;
                            case "5":
                                grid = 2;
                                break;
                            case "4x4":
                                grid = 3;
                                break;
                            case "2x2":
                                grid = 4;
                                break;
                        }

                        records[i] = new Product
                        {
                            Id = i + 1,
                            Code = int.Parse(Txt_Code.Text),
                            Name = Txt_Description.Text,
                            MaxD = double.Parse(Txt_MaxD.Text) / euFactor,
                            MinD = double.Parse(Txt_MinD.Text) / euFactor,
                            MaxOvality = double.Parse(Txt_Ovality.Text),
                            MaxCompacity = double.Parse(Txt_Compacity.Text),
                            Grid = grid
                        };

                        break;
                    }
                }
            }

            using (var writer = new StreamWriter(new FileStream(csvPath, FileMode.Open), System.Text.Encoding.UTF8))
            using (var csvWriter = new CsvWriter(writer, CultureInfo.CurrentCulture))
            {
                csvWriter.WriteRecords(records);
            }

            MessageBox.Show("Product suceesfully updated");
        }

        private void Cmd_Save_Click(object sender, EventArgs e)
        {
            changeProductSetPoint();
        }

        private void changeProductSetPoint()
        {
            Txt_MaxDiameter.Text = Txt_MaxD.Text;
            maxDiameter = double.Parse(Txt_MaxD.Text) / euFactor;

            Txt_MinDiameter.Text = Txt_MinD.Text;
            minDiameter = double.Parse(Txt_MinD.Text) / euFactor;

            Txt_MaxOvality.Text = Txt_Ovality.Text;
            maxOvality = double.Parse(Txt_Ovality.Text);

            Txt_MaxCompacity.Text = Txt_Compacity.Text;
            maxCompactness = double.Parse(Txt_Compacity.Text);

            int grid = 0;

            switch (CmbGrid.SelectedItem.ToString())
            {
                case "3x3":
                    grid = 1;
                    break;
                case "5":
                    grid = 2;
                    break;
                case "4x4":
                    grid = 3;
                    break;
                case "2x2":
                    grid = 4;
                    break;
            }

            updateGridType(grid, CmbGrid.SelectedItem.ToString());

            MessageBox.Show("Set Point Changed Succesfuly");

        }

        private void CmdDelete_Click(object sender, EventArgs e)
        {

        }

        private void CmdAdd_Click(object sender, EventArgs e)
        {
            var records = new List<Product>();

            using (var reader = new StreamReader(new FileStream(csvPath, FileMode.Open), System.Text.Encoding.UTF8))
            using (var csvReader = new CsvReader(reader, CultureInfo.CurrentCulture))
            {
                records = csvReader.GetRecords<Product>().ToList();
                List<int> ids = new List<int>();
                List<int> codes = new List<int>();

                foreach (var record in records)
                {
                    ids.Add(record.Id);
                    codes.Add(record.Code);
                }

                if (!codes.Contains(int.Parse(Txt_Code.Text)))
                {
                    int grid = 0;

                    switch (CmbGrid.SelectedItem.ToString())
                    {
                        case "3x3":
                            grid = 1;
                            break;
                        case "5":
                            grid = 2;
                            break;
                        case "4x4":
                            grid = 3;
                            break;
                        case "2x2":
                            grid = 4;
                            break;
                    }

                    records.Add(new Product
                    {
                        Id = ids.Count + 1,
                        Code = int.Parse(Txt_Code.Text),
                        Name = Txt_Description.Text,
                        MaxD = double.Parse(Txt_MaxD.Text) / euFactor,
                        MinD = double.Parse(Txt_MinD.Text) / euFactor,
                        MaxOvality = double.Parse(Txt_Ovality.Text),
                        MaxCompacity = double.Parse(Txt_Compacity.Text),
                        Grid = grid
                    });
                    MessageBox.Show("Product succesfully added");
                }
                else
                {
                    MessageBox.Show("Code already exist");
                }
            }
            using (var writer = new StreamWriter(new FileStream(csvPath, FileMode.Open), System.Text.Encoding.UTF8))
            using (var csvWriter = new CsvWriter(writer, CultureInfo.CurrentCulture))
            {
                csvWriter.WriteRecords(records);
            }
            using (var reader = new StreamReader(new FileStream(csvPath, FileMode.Open), System.Text.Encoding.UTF8))
            using (var csvReader = new CsvReader(reader, CultureInfo.CurrentCulture))
            {
                CmbProducts.Items.Clear();
                var records2 = csvReader.GetRecords<Product>();
                foreach (var record in records2)
                {
                    CmbProducts.Items.Add(record.Code);
                }
            }
        }

        private void logoffToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (authenticated)
            {
                configurationPage.Enabled = false;
                advancedPage.Enabled = false;
                authenticated = false;
                MessageBox.Show("Logged Off");
            }
            else
            {
                MessageBox.Show("You aren't logged");
            }
        }

        private void loginToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //if (!authenticated)
            //{
            //    // Set new acquisition parameters
            //    LoginDlg loginDlg = new LoginDlg(user);

            //    if (loginDlg.ShowDialog() == DialogResult.OK)
            //    {
            //        authenticated = true;
            //        configurationPage.Enabled = true;
            //        advancedPage.Enabled = true;
            //        MessageBox.Show("Authentication Succesfull");
            //    }
            //    else
            //    {
            //        MessageBox.Show("Authentication Failed");
            //    }
            //}
            //else
            //{
            //    MessageBox.Show("You're already logged");
            //}

        }

        private void CmbProducts_SelectedIndexChanged_1(object sender, EventArgs e)
        {

        }

        private void txtAvgMinD_Click(object sender, EventArgs e)
        {

        }

        private void label2_Click(object sender, EventArgs e)
        {

        }

        private void btnSetPointPLC_Click(object sender, EventArgs e)
        {
            btnSetPointPLC.Enabled = false;
            btnSetPointPLC.BackColor = Color.LightGreen;
            btnSetPointManual.Enabled = true;
            btnSetPointManual.BackColor = Color.Silver;
            btnSetPointLocal.Enabled = true;
            btnSetPointLocal.BackColor = Color.Silver;

            operationMode = 2;
            productsPage.Enabled = false;
            GroupActualTargetSize.Enabled = false;
            GroupSelectGrid.Enabled = false;
        }

        private void btnSetPointManual_Click(object sender, EventArgs e)
        {
            btnSetPointPLC.Enabled = true;
            btnSetPointPLC.BackColor = Color.Silver;
            btnSetPointManual.Enabled = false;
            btnSetPointManual.BackColor = Color.LightGreen;
            btnSetPointLocal.Enabled = true;
            btnSetPointLocal.BackColor = Color.Silver;

            operationMode = 0;
            productsPage.Enabled = false;
            GroupActualTargetSize.Enabled = true;
            GroupSelectGrid.Enabled = true;
        }

        private void btnSetPointLocal_Click(object sender, EventArgs e)
        {
            btnSetPointPLC.Enabled = true;
            btnSetPointPLC.BackColor = Color.Silver;
            btnSetPointManual.Enabled = true;
            btnSetPointManual.BackColor = Color.Silver;
            btnSetPointLocal.Enabled = false;
            btnSetPointLocal.BackColor = Color.LightGreen;

            operationMode = 1;
            productsPage.Enabled = true;
            GroupActualTargetSize.Enabled = false;
            GroupSelectGrid.Enabled = false;
            CmbProducts.SelectedIndex = 0;
            changeProductSetPoint();
        }

        private void Txt_MaxCompacity_TextChanged(object sender, EventArgs e)
        {

        }

        private void btnChangeUnitsMm_Click(object sender, EventArgs e)
        {
            btnChangeUnitsMm.BackColor = Color.LightGreen;
            btnChangeUnitsInch.BackColor = Color.Silver;
            updateUnits("mm");
        }

        private void btnChangeUnitsInch_Click(object sender, EventArgs e)
        {
            btnChangeUnitsMm.BackColor = Color.Silver;
            btnChangeUnitsInch.BackColor = Color.LightGreen;
            updateUnits("inch");
        }

        private void btnIncrementRoiWidth_Click(object sender, EventArgs e)
        {
            if (File.Exists(imagesPath + "updatedROI.jpg"))
            {
                int roiWidth = (UserROI.Right - UserROI.Left) + 12;
                if (roiWidth > 1220) roiWidth = (1220);
                if (roiWidth % 2 == 0)
                {
                    UserROI.Left = 640 - roiWidth / 2;
                    UserROI.Right = 640 + roiWidth / 2;
                }
                else
                {
                    UserROI.Left = 640 - (int)(roiWidth / 2) + 1;
                    UserROI.Right = 640 + (int)(roiWidth / 2);
                }

                if (!triggerPLC && mode == 0)
                {
                    settings.ROI_Left = UserROI.Left;
                    settings.ROI_Right = UserROI.Right;
                    txtRoiWidth.Text = roiWidth.ToString();
                    updateROI();
                }
                else
                {
                    MessageBox.Show("Change the operation mode");
                    UserROI.Left = settings.ROI_Left;
                    UserROI.Right = settings.ROI_Right;
                    txtRoiWidth.Text = (settings.ROI_Right - settings.ROI_Left).ToString();
                }
            }
            else
            {
                MessageBox.Show("Please first take a frame");
            }
        }

        private void btnDecrementRoiWidth_Click(object sender, EventArgs e)
        {
            if (File.Exists(imagesPath + "updatedROI.jpg"))
            {
                int roiWidth = (UserROI.Right - UserROI.Left) - 12;
                if (roiWidth < 24) roiWidth = (24);
                if (roiWidth % 2 == 0)
                {
                    UserROI.Left = 640 - roiWidth / 2;
                    UserROI.Right = 640 + roiWidth / 2;
                }
                else
                {
                    UserROI.Left = 640 - (int)(roiWidth / 2) + 1;
                    UserROI.Right = 640 + (int)(roiWidth / 2);
                }

                if (!triggerPLC && mode == 0)
                {
                    settings.ROI_Left = UserROI.Left;
                    settings.ROI_Right = UserROI.Right;
                    txtRoiWidth.Text = roiWidth.ToString();
                    updateROI();
                }
                else
                {
                    MessageBox.Show("Change the operation mode");
                    UserROI.Left = settings.ROI_Left;
                    UserROI.Right = settings.ROI_Right;
                    txtRoiWidth.Text = (settings.ROI_Right - settings.ROI_Left).ToString();
                }
            }
            else
            {
                MessageBox.Show("Please first take a frame");
            }
        }

        private void btnIncrementRoiHeight_Click(object sender, EventArgs e)
        {
            if (File.Exists(imagesPath + "updatedROI.jpg"))
            {
                int roiHeight = (UserROI.Bottom - UserROI.Top) + 12;
                if (roiHeight > 940) roiHeight = (940);
                if (roiHeight % 2 == 0)
                {
                    UserROI.Top = 480 - roiHeight / 2;
                    UserROI.Bottom = 480 + roiHeight / 2;
                }
                else
                {
                    UserROI.Top = 480 - (int)(roiHeight / 2) + 1;
                    UserROI.Bottom = 480 + (int)(roiHeight / 2);
                }

                if (!triggerPLC && mode == 0)
                {
                    settings.ROI_Top = UserROI.Top;
                    settings.ROI_Bottom = UserROI.Bottom;
                    txtRoiHeight.Text = roiHeight.ToString();
                    updateROI();
                }
                else
                {
                    MessageBox.Show("Change the operation mode");
                    UserROI.Top = settings.ROI_Top;
                    UserROI.Bottom = settings.ROI_Bottom;
                    txtRoiHeight.Text = (settings.ROI_Bottom - settings.ROI_Top).ToString();
                }
            }
            else
            {
                MessageBox.Show("Please first take a frame");
            }
        }

        private void btnDecrementRoiHeight_Click(object sender, EventArgs e)
        {
            if (File.Exists(imagesPath + "updatedROI.jpg"))
            {
                int roiHeight = (UserROI.Bottom - UserROI.Top) - 12;
                if (roiHeight < 24) roiHeight = (24);
                if (roiHeight % 2 == 0)
                {
                    UserROI.Top = 480 - roiHeight / 2;
                    UserROI.Bottom = 480 + roiHeight / 2;
                }
                else
                {
                    UserROI.Top = 480 - (int)(roiHeight / 2) + 1;
                    UserROI.Bottom = 480 + (int)(roiHeight / 2);
                }

                if (!triggerPLC && mode == 0)
                {
                    settings.ROI_Top = UserROI.Top;
                    settings.ROI_Bottom = UserROI.Bottom;
                    txtRoiHeight.Text = roiHeight.ToString();
                    updateROI();
                }
                else
                {
                    MessageBox.Show("Change the operation mode");
                    UserROI.Top = settings.ROI_Top;
                    UserROI.Bottom = settings.ROI_Bottom;
                    txtRoiHeight.Text = (settings.ROI_Bottom - settings.ROI_Top).ToString();
                }
            }
            else
            {
                MessageBox.Show("Please first take a frame");
            }
        }

        private void txtRoiWidth_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)Keys.Enter)
            {
                if (File.Exists(imagesPath + "updatedROI.jpg"))
                {
                    int roiWidth;
                    if (int.TryParse(txtRoiWidth.Text, out roiWidth))
                    {
                        if (roiWidth > 1260) roiWidth = (1260);
                        if (roiWidth < 24) roiWidth = (24);
                        if (roiWidth % 2 == 0)
                        {
                            UserROI.Left = 640 - roiWidth / 2;
                            UserROI.Right = 640 + roiWidth / 2;
                        }
                        else
                        {
                            UserROI.Left = 640 - (int)(roiWidth / 2) + 1;
                            UserROI.Right = 640 + (int)(roiWidth / 2);
                        }

                        if (!triggerPLC && mode == 0)
                        {
                            settings.ROI_Left = UserROI.Left;
                            settings.ROI_Right = UserROI.Right;
                            txtRoiWidth.Text = roiWidth.ToString();
                            updateROI();
                        }
                        else
                        {
                            MessageBox.Show("Change the operation mode");
                            UserROI.Left = settings.ROI_Left;
                            UserROI.Right = settings.ROI_Right;
                            txtRoiWidth.Text = (settings.ROI_Right - settings.ROI_Left).ToString();
                        }
                    }
                    else
                    {
                        MessageBox.Show("Use a valid number");
                        txtRoiWidth.Text = (settings.ROI_Right - settings.ROI_Left).ToString();
                    }

                }
                else
                {
                    MessageBox.Show("Please first take a frame");
                    txtRoiWidth.Text = (settings.ROI_Right - settings.ROI_Left).ToString();
                }
            }
        }

        private void txtRoiHeight_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)Keys.Enter)
            {
                if (File.Exists(imagesPath + "updatedROI.jpg"))
                {
                    int roiHeight;
                    if (int.TryParse(txtRoiHeight.Text, out roiHeight))
                    {
                        if (roiHeight > 940) roiHeight = (940);
                        if (roiHeight < 24) roiHeight = (24);
                        if (roiHeight % 2 == 0)
                        {
                            UserROI.Top = 480 - roiHeight / 2;
                            UserROI.Bottom = 480 + roiHeight / 2;
                        }
                        else
                        {
                            UserROI.Top = 480 - (int)(roiHeight / 2) + 1;
                            UserROI.Bottom = 480 + (int)(roiHeight / 2);
                        }

                        if (!triggerPLC && mode == 0)
                        {
                            settings.ROI_Top = UserROI.Top;
                            settings.ROI_Bottom = UserROI.Bottom;
                            txtRoiHeight.Text = roiHeight.ToString();
                            updateROI();
                        }
                        else
                        {
                            MessageBox.Show("Change the operation mode");
                            UserROI.Top = settings.ROI_Top;
                            UserROI.Bottom = settings.ROI_Bottom;
                            txtRoiHeight.Text = (settings.ROI_Bottom - settings.ROI_Top).ToString();
                        }
                    }
                    else
                    {
                        MessageBox.Show("Use a valid number");
                        txtRoiHeight.Text = (settings.ROI_Bottom - settings.ROI_Top).ToString();
                    }

                }
                else
                {
                    MessageBox.Show("Please first take a frame");
                    txtRoiHeight.Text = (settings.ROI_Bottom - settings.ROI_Top).ToString();
                }
            }
        }

        private void btnFreezeFrame_Click(object sender, EventArgs e)
        {
            if (freezeFrame)
            {
                freezeFrame = false;
                btnFreezeFrame.BackColor = Color.Silver;
                btnFreezeFrame.Text = "FREEZE FRAME";
            }
            else
            {
                freezeFrame = true;
                btnFreezeFrame.BackColor = Color.LightGreen;
                btnFreezeFrame.Text = "FREEZED";
            }
        }

        private void btnManualThreshold_Click(object sender, EventArgs e)
        {
            btnManualThreshold.BackColor = Color.LightGreen;
            btnAutoThreshold.BackColor = Color.Silver;
            autoThreshold = false;
        }

        private void btnAutoThreshold_Click(object sender, EventArgs e)
        {
            btnManualThreshold.BackColor = Color.Silver;
            btnAutoThreshold.BackColor = Color.LightGreen;
            autoThreshold = true; ;
        }

        private void txtAlpha_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)Keys.Enter)
            {
                float value;
                if (float.TryParse(txtAlpha.Text, out value))
                {
                    if (value >= 0 && value <= 1)
                    {
                        alpha = value;
                        settings.alpha = alpha;
                        settings.Save();
                        MessageBox.Show("Data saved");
                    }
                    else
                    {
                        MessageBox.Show("Please use a valid number");
                        txtAlpha.Text = alpha.ToString();
                    }
                }
                else
                {
                    MessageBox.Show("Please use a valid number");
                    txtAlpha.Text = alpha.ToString();
                }
            }
        }

        private void txtMinBlobObjects_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)Keys.Enter)
            {
                int value;
                if (int.TryParse(txtMinBlobObjects.Text, out value))
                {
                    if (value >= 0 && value <= 20)
                    {
                        minBlobObjects = value;
                        settings.minBlobObjects = minBlobObjects;
                        settings.Save();
                        MessageBox.Show("Data saved");
                    }
                    else
                    {
                        MessageBox.Show("Please use a valid number");
                        txtMinBlobObjects.Text = minBlobObjects.ToString();
                    }
                }
                else
                {
                    MessageBox.Show("Please use a valid number");
                    txtMinBlobObjects.Text = minBlobObjects.ToString();
                }
            }
        }

        private void btnCalibrateByHeight_Click(object sender, EventArgs e)
        {
            if (true)
            {
                using (var inputForm = new InputDlg2(units))
                {
                    if (inputForm.ShowDialog() == DialogResult.OK)
                    {
                        double height = inputForm.cameraHeight;

                        double fov = 2 * Math.Atan(lenWidth / (2 * lenF));

                        fov = 2 * Math.Tan(fov / 2) * height;

                        euFactor = fov / 1280;
                        settings.EUFactor = euFactor;
                        euFactorTxt.Text = Math.Round(euFactor, 3).ToString();
                        maxDiameter = double.Parse(Txt_MaxDiameter.Text) / euFactor;
                        minDiameter = double.Parse(Txt_MinDiameter.Text) / euFactor;
                        settings.maxDiameter = maxDiameter;
                        settings.minDiameter = minDiameter;

                        MessageBox.Show("Calibration Succesful, Factor: " + euFactor);
                    }
                }
            }
            else
            {
                MessageBox.Show("Change the operation mode");
            }
        }

        private void btnTest_Click(object sender, EventArgs e)
        {
            //PrintDeviceList();
            //trigger();
        }

        

        private void viewModeBtn_Click(object sender, EventArgs e)
        {
            if (mode == 0)
            {
                mode = 1; // Live

                StopLive();

                virtualTriggerBtn.Enabled = false;
                virtualTriggerBtn.BackColor = Color.DarkGray;
                triggerModeBtn.Enabled = false;
                triggerModeBtn.BackColor = Color.DarkGray;
                txtViewMode.Text = "LIVE";
                txtViewMode.BackColor = Color.LightGreen;
                processImageBtn.BackColor = Color.DarkGray;
                processImageBtn.Enabled = false;

                propertyMap.SetValue(ic4.PropId.TriggerMode, "Off");
                triggerSoftware = false;
                display.BringToFront();
                display.Visible = true;

                StartLive();
            }
            else
            {
                mode = 0; // Frame

                StopLive();

                virtualTriggerBtn.Enabled = true;
                virtualTriggerBtn.BackColor = Color.Silver;
                triggerModeBtn.Enabled = true;
                triggerModeBtn.BackColor = Color.Silver;
                txtViewMode.Text = "FRAME";
                txtViewMode.BackColor = Color.Khaki;

                propertyMap.SetValue(ic4.PropId.TriggerMode, "On");
                triggerSoftware = true;
                display.SendToBack();
                display.Visible = false;

                StartLive();
            }

            

        }

        bool IsRunning
        {
            get
            {
                return grabber.IsAcquisitionActive && grabber.IsStreaming;
            }
        }

        bool IsValid
        {
            get
            {
                return grabber.IsDeviceOpen && grabber.IsDeviceValid;
            }
        }

        void StartLive()
        {
            if (IsValid)
            {
                if (IsRunning)
                {
                    return;
                }

                grabber.StreamSetup(queueSink, ic4.StreamSetupOption.AcquisitionStart);
            }
        }

        void StopLive()
        {
            if (IsValid)
            {
                if (IsRunning)
                {
                    grabber.StreamStop();
                }
            }
        }

        private void videoSettingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ic4.WinForms.Dialogs.ShowDevicePropertyDialog(grabber, this);
        }
    }
}
