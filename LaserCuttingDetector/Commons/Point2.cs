using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;

namespace LaserCuttingDetector.Commons
{
    public class Point2 : INotifyPropertyChanged
    {
        private double _x;
        private double _y;
        private bool _isSelected;

        public double X
        {
            get => _x;
            set
            {
                if (Math.Abs(_x - value) > double.Epsilon)
                {
                    _x = value;
                    OnPropertyChanged();
                }
            }
        }

        public double Y
        {
            get => _y;
            set
            {
                if (Math.Abs(_y - value) > double.Epsilon)
                {
                    _y = value;
                    OnPropertyChanged();
                }
            }
        }

        [XmlIgnore]
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

        public Point2() { }

        public Point2(double x, double y)
        {
            _x = x;
            _y = y;
        }

        public override string ToString()
        {
            return $"X:{X:F2}；Y:{Y:F2}";
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
