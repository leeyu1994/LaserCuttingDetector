using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CadDataExtractor
{
    using System.ComponentModel;
    // 这是一个辅助类，用于表示图层列表中的每一项
    // INotifyPropertyChanged 接口是WPF数据绑定的核心，当属性值改变时，它会通知UI更新
    public class LayerItemViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;
        public string Name { get; set; }
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
