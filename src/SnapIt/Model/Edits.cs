using System;
using System.Windows;
using System.Windows.Controls;

namespace SnapIt.Model
{
    public abstract class EditCommand
    {
        public abstract void Undo();
        public abstract void Redo();
    }

    /// <summary>图元添加命令（撤销=移除，重做=重新添加）。</summary>
    public class ShapeAddCommand : EditCommand
    {
        private readonly Canvas _parent;
        private readonly UIElement _element;

        public ShapeAddCommand(Canvas parent, UIElement element)
        {
            _parent = parent;
            _element = element;
        }

        public override void Undo() { _parent.Children.Remove(_element); }
        public override void Redo() { if (!_parent.Children.Contains(_element)) _parent.Children.Add(_element); }
    }

    /// <summary>通用命令：用闭包捕获撤销/重做动作。</summary>
    public class DelegateCommand : EditCommand
    {
        private readonly Action _undo;
        private readonly Action _redo;

        public DelegateCommand(Action undo, Action redo)
        {
            _undo = undo;
            _redo = redo;
        }

        public override void Undo() { _undo?.Invoke(); }
        public override void Redo() { _redo?.Invoke(); }
    }
}
