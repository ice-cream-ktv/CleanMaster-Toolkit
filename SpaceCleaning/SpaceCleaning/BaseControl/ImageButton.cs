using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SpaceCleaning.BaseControl
{
    public class ImageButton : Button
    {
        public ImageSource Normal
        {
            get { return (ImageSource)GetValue(NormalProperty); }
            set { SetValue(NormalProperty, value); }
        }

        public static readonly DependencyProperty NormalProperty =
            DependencyProperty.Register("Normal", typeof(ImageSource), typeof(ImageButton));

        public ImageSource Hover
        {
            get { return (ImageSource)GetValue(HoverProperty); }
            set { SetValue(HoverProperty, value); }
        }

        public static readonly DependencyProperty HoverProperty =
            DependencyProperty.Register("Hover", typeof(ImageSource), typeof(ImageButton));

        public ImageSource Pressed
        {
            get { return (ImageSource)GetValue(PressedProperty); }
            set { SetValue(PressedProperty, value); }
        }

        public static readonly DependencyProperty PressedProperty =
            DependencyProperty.Register("Pressed", typeof(ImageSource), typeof(ImageButton));

        // 选中后的效果
        public bool IsSelected
        {
            get { return (bool)GetValue(IsSelectedProperty); }
            set { SetValue(IsSelectedProperty, value); }
        }
        public static readonly DependencyProperty IsSelectedProperty =
            DependencyProperty.Register("IsSelected", typeof(bool), typeof(ImageButton));

    }
}
