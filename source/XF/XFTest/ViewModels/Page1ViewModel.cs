﻿using System;
using System.Collections.Generic;
using System.ComponentModel;



namespace XFTest.ViewModels
{
	public class Page1ViewModel : INotifyPropertyChanged
	{
		FocusState<FocusField> _focusField = FocusField.Password;

		public ModifyPage1ViewModel ModifyViewModel { get; } = new();

		public decimal DecimalProp { get; set; } = 1.230M;

		public bool BooleanProp { get; set; }

		public FocusState<FocusField> FocusedField
		{
			get => _focusField;
			private set
			{
				_focusField = value;
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FocusedField)));
			}
		}

		public IList<EntityViewModel> ListProp { get; } = new List<EntityViewModel>
		{
			new EntityViewModel { DecimalProp = 1, BooleanProp = true },
		};

		public int[] ArrayProp { get; set; } = new[] { 1, 2, 3 };

		public string StringProp => "0000123";

		public Func<Type, char, bool>? FuncProp { get; set; }

		public event PropertyChangedEventHandler? PropertyChanged;

		public void FocusUserName()
		{
			if (FocusedField != FocusField.UserName)
			{
				FocusedField = FocusField.UserName;
			}
		}

		public enum FocusField
		{
			UserName,
			Password
		}
	}

	public class ModifyPage1ViewModel : INotifyPropertyChanged
	{
		public event PropertyChangedEventHandler? PropertyChanged;

		public string? Input1 { get; set; }
		public int? Input2 { get; set; }
	}

	public class EntityViewModel : INotifyPropertyChanged
	{
		public decimal DecimalProp { get; set; }

		public bool BooleanProp { get; set; }

		public event PropertyChangedEventHandler? PropertyChanged;
	}
}
