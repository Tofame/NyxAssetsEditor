using System;
using System.Collections.Generic;
using System.IO;

namespace NyxAssetsEditor.Services.Archive
{
	public class UndoRedoStack<T>
	{
		private readonly List<T> _undoList = new();
		private readonly List<T> _redoList = new();
		private readonly int _maxCapacity;

		public UndoRedoStack(int maxCapacity)
		{
			_maxCapacity = maxCapacity;
		}

		public int UndoCount => _undoList.Count;
		public int RedoCount => _redoList.Count;

		public void Push(T state)
		{
			_undoList.Add(state);
			_redoList.Clear();

			if (_undoList.Count > _maxCapacity)
			{
				var removed = _undoList[0];
				_undoList.RemoveAt(0);
				DisposeState(removed);
			}
		}

		public T? Undo(T currentState)
		{
			if (_undoList.Count == 0)
				return default;

			var previous = _undoList[_undoList.Count - 1];
			_undoList.RemoveAt(_undoList.Count - 1);
			_redoList.Add(currentState);

			if (_redoList.Count > _maxCapacity)
			{
				var removed = _redoList[0];
				_redoList.RemoveAt(0);
				DisposeState(removed);
			}

			return previous;
		}

		public T? Redo(T currentState)
		{
			if (_redoList.Count == 0)
				return default;

			var next = _redoList[_redoList.Count - 1];
			_redoList.RemoveAt(_redoList.Count - 1);
			_undoList.Add(currentState);

			if (_undoList.Count > _maxCapacity)
			{
				var removed = _undoList[0];
				_undoList.RemoveAt(0);
				DisposeState(removed);
			}

			return next;
		}

		public void Clear()
		{
			foreach (var state in _undoList) DisposeState(state);
			foreach (var state in _redoList) DisposeState(state);
			_undoList.Clear();
			_redoList.Clear();
		}

		private void DisposeState(T state)
		{
			if (state is string path && File.Exists(path))
			{
				try
				{
					File.Delete(path);
					var dir = Path.GetDirectoryName(path);
					if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
					{
						Directory.Delete(dir, true);
					}
				}
				catch
				{
					// Ignore
				}
			}
		}
	}
}
