using HalconDotNet;
using System.Drawing;

namespace VisionLibrary.CadIntegration
{
    public class RuntimeLocator
    {
        private readonly CadTemplateTrainer _trainer; // 持有训练好的模型

        public RuntimeLocator(CadTemplateTrainer trainer)
        {
            _trainer = trainer;
        }

        /// <summary>
        /// 对真实图片进行定位
        /// </summary>
        public List<PointF> Locate(HImage sceneImage)
        {
            var resultPoints = new List<PointF>();

            // 1. 粗定位 (找中心)
            // 参数可根据实际情况提取到 Config 中
            _trainer.CoarseModel.FindShapeModel(sceneImage, -0.5, 1.0, 0.5, 1, 0.5, "least_squares", 0, 0.9,
                out HTuple row, out HTuple col, out HTuple angle, out HTuple score);

            if (score.Length == 0) return resultPoints; // 失败

            // 2. 计算变换矩阵 (从 模板中心 -> 实际中心)
            HHomMat2D mat = new HHomMat2D();
            mat.VectorToRigid(
                new HTuple(_trainer.ImageCenter.Y), new HTuple(_trainer.ImageCenter.X), new HTuple(0.0),
                row, col, angle
            );

            // 3. 遍历4个理论角点，进行精定位
            foreach (var corner in _trainer.TheoreticalCorners)
            {
                // 3.1 预测位置
                HTuple expectedRow = mat.AffineTransPoint2d(corner.Y, corner.X, out HTuple expectedCol);

                // 3.2 截取搜索区域 (ROI)
                HRegion searchRegion = new HRegion();
                searchRegion.GenRectangle1(
                    expectedRow - 50, expectedCol - 50, // 假设搜索范围 +/- 50像素
                    expectedRow + 50, expectedCol + 50
                );

                HImage reducedImage = sceneImage.ReduceDomain(searchRegion);

                // 3.3 精定位 (找十字线)
                _trainer.FineModel.FindShapeModel(reducedImage, -0.2, 0.4, 0.4, 1, 0.5, "least_squares", 0, 0.7,
                     out HTuple cRow, out HTuple cCol, out HTuple cAngle, out HTuple cScore);

                if (cScore.Length > 0)
                {
                    resultPoints.Add(new PointF((float)cCol.D, (float)cRow.D)); // Halcon是Row(Y),Col(X)
                }
                else
                {
                    // 没找到则使用预测值
                    resultPoints.Add(new PointF((float)expectedCol.D, (float)expectedRow.D));
                }

                searchRegion.Dispose();
                reducedImage.Dispose();
            }

            return resultPoints;
        }
    }
}
