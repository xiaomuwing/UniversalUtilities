using System;
using System.Linq;
namespace UniversalUtilities
{
    /// <summary>
    /// 高斯滤波
    /// </summary>
    public static class GaussianFilter
    {
        /// <summary>
        /// 高斯滤波
        /// </summary>
        /// <param name="data"></param>
        /// <param name="stdDev"></param>
        /// <param name="kernelSize"></param>
        /// <returns></returns>
        public static double[] Gaussian(double[] data, double stdDev, int kernelSize)
        {
            double[] kernel = new double[kernelSize];
            for (int i = 0; i < kernelSize; i++)
            {
                double x = i - (kernelSize - 1) / 2;
                kernel[i] = GaussianKernel(stdDev, x);
            }
            double kernelSum = kernel.Sum();
            for (int i = 0; i < kernelSize; i++)
            {
                kernel[i] /= kernelSum;
            }
            return Convolve(data, kernel);
        }
        static double[] Convolve(double[] data, double[] kernel)
        {
            int dataLength = data.Length;
            int kernelLength = kernel.Length;
            double[] result = new double[dataLength];
            for (int i = 0; i < dataLength; i++)
            {
                double sum = 0;
                for (int j = 0; j < kernelLength; j++)
                {
                    int dataIndex = i - j + (kernelLength - 1) / 2;
                    if (dataIndex >= 0 && dataIndex < dataLength)
                    {
                        sum += data[dataIndex] * kernel[j];
                    }
                }
                result[i] = sum;
            }
            Array.Copy(data, 0, result, 0, kernelLength / 2);
            Array.Copy(data, dataLength - kernelLength / 2, result, dataLength - kernelLength / 2, kernelLength / 2);
            return result;
        }
        static double GaussianKernel(double stdDev, double x)
        {
            double exponent = Math.Pow(x, 2) / (2 * Math.Pow(stdDev, 2));
            double coefficient = 1 / (stdDev * Math.Sqrt(2 * Math.PI));
            double result = coefficient * Math.Exp(0 - exponent);
            return result;
        }
    }
}
