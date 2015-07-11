﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MotionGestureProcessing
{
    class PCA : Process
    {
        private Processing.ImageReadyHandler m_PCAImageHandler;

        public PCA()
        { }

        public void initialize()
        {
            setupListener();
        }

        protected override void setupListener()
        {
            m_PCAImageHandler = (obj) =>
            {
                //Thread t = new Thread(new ParameterizedThreadStart(doWork));
                //t.Start(obj);
                doWork(obj);
            };

            Processing.getInstance().PCAImageFilled += m_PCAImageHandler;
        }

        /// <summary>
        /// What this process does to an image
        /// </summary>
        /// <param name="obj">Data pertaining to this image</param>
        /// <param name="p_image">Image to be processed</param>
        /// <seealso cref="http://www.cs.otago.ac.nz/cosc453/student_tutorials/principal_components.pdf"/>
        protected override async void doWork(Object p_imgData)
        {
            List<Point> dataPoints = ((imageData)p_imgData).Datapoints;

            //Step 2 of PCA Step 1 is gathering data.
            PCAData pcaData = getMeanValues(dataPoints, ((imageData)p_imgData).Filter);
            
            //The point will be null if there were no points to observe
            if (pcaData != null)
            {
                //Readjust center
                ((imageData)p_imgData).Filter = new Rectangle((int)(pcaData.XBar - ((imageData)p_imgData).Filter.Width),
                                                              (int)(pcaData.YBar - ((imageData)p_imgData).Filter.Height),
                                                              ((imageData)p_imgData).Filter.Width,
                                                              ((imageData)p_imgData).Filter.Height);
                //step 3 develop the coVariance matrix
                genCoVarMatrix(dataPoints, ref pcaData);

                //step 4 get eigenvectors and values
                calculateEigens(ref pcaData);

                //Draw the center with new axes
                byte[] buffer;
                BitmapData data = lockBitmap(out buffer, ((imageData)p_imgData).Image);
                drawOrientation(buffer, data, pcaData.eigenVectors, new Point((int)pcaData.XBar, (int)pcaData.YBar));                
                unlockBitmap(ref buffer, ref data, ((imageData)p_imgData).Image);

                //Adjust datapoints
                //adjustDatapoints(ref dataPoints, pcaData);

                //((imageData)p_imgData).Datapoints = dataPoints;
                ((imageData)p_imgData).Orientation = getOrientation(pcaData.eigenVectors);
            }

            Processing.getInstance().ToGesturesImage = (imageData)p_imgData;
        }

        /// <summary>
        /// Gather info about the dataPoints
        /// </summary>
        /// <param name="p_dataPoints">dataset to evaluate</param>
        private PCAData getMeanValues(List<Point> p_dataPoints, Rectangle p_filter)
        {
            if (p_dataPoints.Count == 0)
                return null;

            PCAData pcaData = new PCAData();

            //Populate data members of the PCAData object
            /*Parallel.ForEach(p_dataPoints, point =>
                {
                    pcaData.Sumx += point.X;
                    pcaData.Sumy += point.Y;
                });
            */
            foreach(Point point in p_dataPoints)
            {
                pcaData.Sumx += point.X;
                pcaData.Sumy += point.Y;
            }

            pcaData.N = p_dataPoints.Count;
            pcaData.XBar = pcaData.Sumx / pcaData.N;
            pcaData.YBar = pcaData.Sumy / pcaData.N;

            return pcaData;
        }

        /// <summary>
        /// This method is designed to create a covariance matrix.
        /// </summary>
        /// <param name="p_pcaData"></param>
        private void genCoVarMatrix(List<Point> p_dataPoints, ref PCAData p_pcaData)
        {
            //Sum up the mean product
            /*Parallel.ForEach(p_dataPoints, point =>
                {
                    p_pcaData.XVar = (point.X - p_pcaData.XBar) * (point.X - p_pcaData.XBar);
                    p_pcaData.YVar = (point.Y - p_pcaData.YBar) * (point.Y - p_pcaData.YBar);
                    p_pcaData.CoVar = (point.X - p_pcaData.XBar) * (point.Y - p_pcaData.YBar);
                });*/

            foreach (Point point in p_dataPoints)
            {
                p_pcaData.XVar += (point.X - p_pcaData.XBar) * (point.X - p_pcaData.XBar);
                p_pcaData.YVar += (point.Y - p_pcaData.YBar) * (point.Y - p_pcaData.YBar);
                p_pcaData.CoVar += (point.X - p_pcaData.XBar) * (point.Y - p_pcaData.YBar);
            }

            //perform the (n-1) division
            p_pcaData.XVar /= (p_pcaData.N - 1);
            p_pcaData.YVar /= (p_pcaData.N - 1);
            p_pcaData.CoVar /= (p_pcaData.N - 1);

        }

        /// <summary>
        /// This function first find the Eigen Values using discriminants
        /// 
        /// </summary>
        /// <param name="p_pcaData"></param>
        private void calculateEigens(ref PCAData p_pcaData)
        {
            /* let Oab equal covariance of ab so Oaa would be the variance
             * This is after the A - lambda*I 
             *  Oxx - lambda, Oxy
             *  Oxy,          Oyy - lambda
             *  find the determinant
             *  
             * lambda^2 + (-Oyy - Oxx)lambda + (OxxOyy - Oxy^2)
             */

            //The following performs the quadratic formula on the above function
            double diffVar = -p_pcaData.YVar - p_pcaData.XVar;   
            double discriminant = Math.Sqrt(diffVar * diffVar -
                                            4 * (p_pcaData.XVar * p_pcaData.YVar - p_pcaData.CoVar * p_pcaData.CoVar));
            double lambdaPlus = (-diffVar + discriminant) / 2;
            double lambdaMinus = (-diffVar - discriminant) / 2;

            p_pcaData.eigenValues[0] = lambdaPlus;
            p_pcaData.eigenValues[1] = lambdaMinus;

            //Now we have the eigen values time to get the eigenvectors that match them
            /*
             *  Oxx - lambda, Oxy
             *  Oxy,          Oyy - lambda
             * 
             * convert to  RRE format
             * 
             * x, y
             * 1, 0 : -Oxy / (Oxx - lambda)
             * 0, 1 : -Oxy / (Oyy - lambda)
             */

            //for some reason when the coVar is positive my primary is correct and secondary isn't normal
            // and when it's negative secondary is correct and primary isn't normal
            if (p_pcaData.CoVar > 0)
            {
                //first principal component
                p_pcaData.eigenVectors[0, 0] = p_pcaData.CoVar / (p_pcaData.XVar - p_pcaData.eigenValues[0]);
                p_pcaData.eigenVectors[0, 1] = p_pcaData.CoVar / (p_pcaData.YVar - p_pcaData.eigenValues[0]);

                //second is normal to the first
                p_pcaData.eigenVectors[1, 0] = -p_pcaData.eigenVectors[0, 1];
                p_pcaData.eigenVectors[1, 1] = p_pcaData.eigenVectors[0, 0];
            }
            else
            {
                //second principal component
                p_pcaData.eigenVectors[1, 0] = p_pcaData.CoVar / (p_pcaData.XVar - p_pcaData.eigenValues[1]);
                p_pcaData.eigenVectors[1, 1] = p_pcaData.CoVar / (p_pcaData.YVar - p_pcaData.eigenValues[1]);

                //first is normal to second
                p_pcaData.eigenVectors[0, 0] = -p_pcaData.eigenVectors[1, 1];
                p_pcaData.eigenVectors[0, 1] = p_pcaData.eigenVectors[1, 0];
            }
            //normalize component
            //normX = x / sqrt(x*x + y*y) normY = y / sqrt(x*x + y*y)
            double len = Math.Sqrt(p_pcaData.eigenVectors[0, 0] * p_pcaData.eigenVectors[0, 0] +
                                   p_pcaData.eigenVectors[0, 1] * p_pcaData.eigenVectors[0, 1]);
            p_pcaData.eigenVectors[0, 0] /= len;
            p_pcaData.eigenVectors[0, 1] /= len;

            len = Math.Sqrt(p_pcaData.eigenVectors[1, 0] * p_pcaData.eigenVectors[1, 0] +
                            p_pcaData.eigenVectors[1, 1] * p_pcaData.eigenVectors[1, 1]);
            p_pcaData.eigenVectors[1, 0] /= len;
            p_pcaData.eigenVectors[1, 1] /= len;
        }

        /// <summary>
        /// NOT USED FOR ISSUES IN GESTURES
        /// 
        /// this transforms the original data by multiplying the eigenvectors by the mean adjusted data
        /// The eigenvalues are generally stored in a matrix like so | x1, x2 | and so must be transposed
        ///                                                          | y1, y2 | 
        /// However I've already stored it as transposed so that won't happen.  Second the point matrix doesn't exist
        /// per say, I'll explain the implementation later
        /// </summary>
        /// <param name="p_dataPoints">points to transform</param>
        /// <param name="p_pcaData">holds the eigenvectors</param>
        private void adjustDatapoints(ref List<Point> p_dataPoints, PCAData p_pcaData)
        {
            //Lists are immutable so i need to create a new list in order to work out the transform
            List<Point> transPoints = new List<Point>();

            double x1, y1, x2, y2;
            float xbar, ybar;
            x1 = p_pcaData.eigenVectors[0, 0];
            y1 = p_pcaData.eigenVectors[0, 1];
            x2 = p_pcaData.eigenVectors[1, 0];
            y2 = p_pcaData.eigenVectors[1, 1];

            xbar = p_pcaData.XBar;
            ybar = p_pcaData.YBar;

            //performs a matrix multiply 
            foreach (Point point in p_dataPoints)
            {
                Point insert = new Point();

                insert.X = (int)(x1 * (point.X - xbar) + y1 * (point.Y - ybar));
                insert.Y = (int)(x2 * (point.X - xbar) + y2 * (point.Y - ybar));

                transPoints.Add(insert);
            }

            p_dataPoints = transPoints;
        }

        /// <summary>
        /// Use the principal component to determine the orientation
        /// The top of the image represents 0 deg
        /// 
        /// This methods use the law of Cosines a^2 = b^2 + c^2 - 2bc * cos(A)
        /// b = c = 1
        /// thus 
        /// a^2 = 2 - 2 * cos(A)
        /// a^2 = (0 - x)(0 - x) + (-1--y)(-1--y)
        /// a^2 = x^2 + (y-1)(y-1)
        /// 2 * cos(A) = 2 - x^2 + (y-1)(y-1)
        /// cos(A) = (2 - x^2 + (y-1)(y-1)) / 2
        /// A = Acos((2 - x^2 + (y-1)(y-1)) / 2)
        /// </summary>
        /// <param name="p_vectors"></param>
        /// <returns></returns>
        private double getOrientation(double[,] p_vectors)
        {
            double x = p_vectors[0, 0];
            double y = p_vectors[0, 1];

            double a2 = x * x + (y + 1) * (y + 1);

            double rads = Math.Acos(1 - a2 / 2);

            //convert to degrees
            if (x > 0)
                return rads / Math.PI * 180;
            else
                return -rads / Math.PI * 180;
        }

        /// <summary>
        /// struct for PCA data.  
        /// <remarks>volatile variable can only be int or float
        /// becuase of this I end up with mixed data types.
        /// </remarks>
        /// </summary>
        private class PCAData
        {
            public int N { get; set; }
            private volatile int m_sumx;
            private volatile int m_sumy;
            private double m_xvar;
            private double m_yvar;
            private double m_covar;

            public int Sumx {
                get { return m_sumx; }
                set { m_sumx = value; }
            }
            public int Sumy {
                get { return m_sumy; }
                set { m_sumy = value; }
            }
            public float XBar { get; set; }
            public float YBar { get; set; }
            public double XVar {
                get { return m_xvar; }
                set { m_xvar = value; }
            }
            public double YVar {
                get { return m_yvar; }
                set { m_yvar = value; }
            }
            public double CoVar {
                get { return m_covar; }
                set { m_covar = value; }
            }
            
            public double[] eigenValues = new double[2];
            public double[,] eigenVectors = new double[2, 2];
        }
    }

}
