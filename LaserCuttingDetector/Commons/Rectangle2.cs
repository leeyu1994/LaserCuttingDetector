using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LaserCuttingDetector.Commons
{
    // 添加Rectangle2类定义
    public class Rectangle2 : INotifyPropertyChanged
    {
        private double _centerX;
        private double _centerY;
        private double _width;
        private double _height;
        private double _angle;
        private bool _isSelected;

        public double CenterX
        {
            get => _centerX;
            set
            {
                if (Math.Abs(_centerX - value) > 0.001)
                {
                    _centerX = value;
                    OnPropertyChanged();
                }
            }
        }

        public double CenterY
        {
            get => _centerY;
            set
            {
                if (Math.Abs(_centerY - value) > 0.001)
                {
                    _centerY = value;
                    OnPropertyChanged();
                }
            }
        }

        public double Width
        {
            get => _width;
            set
            {
                if (Math.Abs(_width - value) > 0.001)
                {
                    _width = value;
                    OnPropertyChanged();
                }
            }
        }

        public double Height
        {
            get => _height;
            set
            {
                if (Math.Abs(_height - value) > 0.001)
                {
                    _height = value;
                    OnPropertyChanged();
                }
            }
        }

        public double Angle
        {
            get => _angle;
            set
            {
                if (Math.Abs(_angle - value) > 0.001)
                {
                    _angle = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

}
