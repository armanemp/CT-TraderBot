using System;
using System.Collections.Generic;
using System.Globalization;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo.Robots
{
    public partial class SmartPositionAutoTraderM5MicroSwingPrecisionHedgedVolume
    {
        private void CreateVolumeControlPanel()
        {
            _manualVolumeTextBox = new TextBox
            {
                Text = _runtimeManualVolumeLots.ToString("0.##", CultureInfo.InvariantCulture),
                Width = 74,
                Height = 24,
                FontSize = 11,
                ForegroundColor = Color.White,
                BackgroundColor = Color.FromHex("#232A36"),
                Margin = new Thickness(3)
            };

            _freeMarginUsageTextBox = new TextBox
            {
                Text = _runtimeMaxFreeMarginUsagePercent.ToString("0.#", CultureInfo.InvariantCulture),
                Width = 58,
                Height = 24,
                FontSize = 11,
                ForegroundColor = Color.White,
                BackgroundColor = Color.FromHex("#232A36"),
                Margin = new Thickness(3)
            };

            _manualModeButton = new Button
            {
                Text = "MANUAL LOTS",
                Width = 112,
                Height = 26,
                FontSize = 10,
                ForegroundColor = Color.White,
                BackgroundColor = Color.FromHex("#3A4658"),
                Margin = new Thickness(3)
            };
            _manualModeButton.Click += args =>
            {
                _runtimeVolumeMode = MicroSwingPrecisionVolumeMode.ManualLots;
                UpdateVolumeControlPanelStatus();
                SetVolumeNotice("Manual mode selected. The selected lot size is used as-is or the entry is skipped if risk/margin limits would be exceeded.", false);
            };

            _autoModeButton = new Button
            {
                Text = "AUTO MARGIN",
                Width = 105,
                Height = 26,
                FontSize = 10,
                ForegroundColor = Color.White,
                BackgroundColor = Color.FromHex("#3A4658"),
                Margin = new Thickness(3)
            };
            _autoModeButton.Click += args =>
            {
                _runtimeVolumeMode = MicroSwingPrecisionVolumeMode.AutoFreeMargin;
                UpdateVolumeControlPanelStatus();
                SetVolumeNotice("Auto mode selected. Volume is recalculated on every entry from live free margin, stop distance, setup risk budget and symbol limits.", false);
            };

            var header = new TextBlock
            {
                Text = "MICRO-SWING | VOLUME",
                FontSize = 12,
                FontWeight = FontWeight.Bold,
                ForegroundColor = Color.FromHex("#E5EDF7"),
                Margin = new Thickness(4)
            };

            var modeRow = new StackPanel { Orientation = Orientation.Horizontal };
            modeRow.AddChild(_manualModeButton);
            modeRow.AddChild(_autoModeButton);

            var manualRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2) };
            manualRow.AddChild(new TextBlock
            {
                Text = "Manual lots",
                Width = 92,
                FontSize = 11,
                ForegroundColor = Color.FromHex("#C7D0DD"),
                VerticalAlignment = VerticalAlignment.Center
            });
            manualRow.AddChild(_manualVolumeTextBox);

            var marginRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2) };
            marginRow.AddChild(new TextBlock
            {
                Text = "Auto free margin %",
                Width = 125,
                FontSize = 11,
                ForegroundColor = Color.FromHex("#C7D0DD"),
                VerticalAlignment = VerticalAlignment.Center
            });
            marginRow.AddChild(_freeMarginUsageTextBox);

            var applyButton = new Button
            {
                Text = "APPLY VALUES",
                Width = 216,
                Height = 26,
                FontSize = 10,
                ForegroundColor = Color.White,
                BackgroundColor = Color.FromHex("#245A7A"),
                Margin = new Thickness(3)
            };
            applyButton.Click += args => ApplyVolumePanelValues();

            _volumeModeStatusText = new TextBlock
            {
                FontSize = 11,
                ForegroundColor = Color.FromHex("#9FE3C2"),
                Margin = new Thickness(4)
            };
            _volumeAccountStatusText = new TextBlock
            {
                FontSize = 10,
                ForegroundColor = Color.FromHex("#CAD2DE"),
                Margin = new Thickness(4)
            };
            _entryStatusText = new TextBlock
            {
                Text = "Entry: waiting for a valid signal.",
                FontSize = 10,
                ForegroundColor = Color.FromHex("#B8C2D0"),
                Margin = new Thickness(4)
            };
            _volumeNoticeText = new TextBlock
            {
                Text = "Ready. TP remains a chart-only guide.",
                FontSize = 10,
                ForegroundColor = Color.FromHex("#B8C2D0"),
                Margin = new Thickness(4)
            };

            _volumePanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Width = 238,
                BackgroundColor = Color.FromHex("#141922"),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(8),
                Opacity = 0.96
            };
            _volumePanel.AddChild(header);
            _volumePanel.AddChild(modeRow);
            _volumePanel.AddChild(manualRow);
            _volumePanel.AddChild(marginRow);
            _volumePanel.AddChild(applyButton);
            _volumePanel.AddChild(_volumeModeStatusText);
            _volumePanel.AddChild(_volumeAccountStatusText);
            _volumePanel.AddChild(_entryStatusText);
            _volumePanel.AddChild(_volumeNoticeText);
            Chart.AddControl(_volumePanel);
            UpdateVolumeControlPanelStatus();
            SetVolumeNotice("Manual uses your entered lots; Auto recalculates from live free margin and risk before every entry.", false);
        }

        private void ApplyVolumePanelValues()
        {
            double manualLots;
            double marginUsage;
            if (!TryParsePanelNumber(_manualVolumeTextBox == null ? string.Empty : _manualVolumeTextBox.Text, out manualLots) ||
                !IsFinitePositive(manualLots) || manualLots < 0.01 || manualLots > 100.0)
            {
                SetVolumeNotice("Invalid manual lot size. Enter a value from 0.01 to 100 lots.", true);
                return;
            }

            if (!TryParsePanelNumber(_freeMarginUsageTextBox == null ? string.Empty : _freeMarginUsageTextBox.Text, out marginUsage) ||
                double.IsNaN(marginUsage) || double.IsInfinity(marginUsage) ||
                marginUsage < 5.0 || marginUsage > 80.0)
            {
                SetVolumeNotice("Invalid Auto margin usage. Enter a percentage from 5 to 80 to keep free-margin reserve.", true);
                return;
            }

            double rawManualUnits = Symbol.QuantityToVolumeInUnits(manualLots);
            double manualUnits = Symbol.NormalizeVolumeInUnits(rawManualUnits, RoundingMode.Down);
            if (manualUnits < Symbol.VolumeInUnitsMin || manualUnits > Symbol.VolumeInUnitsMax ||
                Math.Abs(manualUnits - rawManualUnits) > Math.Max(0.000001, Symbol.VolumeInUnitsStep * 0.000001))
            {
                SetVolumeNotice("Manual lots are outside the broker's allowed range or volume step. Adjust the value; it will not be silently rounded down.", true);
                return;
            }

            _runtimeManualVolumeLots = Symbol.VolumeInUnitsToQuantity(manualUnits);
            _runtimeMaxFreeMarginUsagePercent = marginUsage;
            _manualVolumeTextBox.Text = _runtimeManualVolumeLots.ToString("0.##", CultureInfo.InvariantCulture);
            _freeMarginUsageTextBox.Text = _runtimeMaxFreeMarginUsagePercent.ToString("0.#", CultureInfo.InvariantCulture);
            UpdateVolumeControlPanelStatus();
            SetVolumeNotice("Values applied for this running cBot instance. For restart defaults, also update the cBot parameters.", false);
        }

        private bool TryParsePanelNumber(string raw, out double value)
        {
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return true;
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        private void UpdateVolumeControlPanelStatus()
        {
            if (_volumeModeStatusText == null || _volumeAccountStatusText == null)
                return;

            bool manual = _runtimeVolumeMode == MicroSwingPrecisionVolumeMode.ManualLots;
            _manualModeButton.Text = manual ? "MANUAL ✓" : "MANUAL LOTS";
            _autoModeButton.Text = manual ? "AUTO MARGIN" : "AUTO ✓";
            _volumeModeStatusText.Text = manual
                ? String.Format(CultureInfo.InvariantCulture, "MODE: MANUAL | requested {0:0.##} lots", _runtimeManualVolumeLots)
                : String.Format(CultureInfo.InvariantCulture, "MODE: AUTO | cap {0:0.##} lots | {1:0.#}% free margin", MaximumAutoVolumeLots, _runtimeMaxFreeMarginUsagePercent);

            double freeMargin = Math.Max(0.0, Account.FreeMargin);
            double autoBudget = freeMargin * _runtimeMaxFreeMarginUsagePercent / 100.0;
            _volumeAccountStatusText.Text = String.Format(CultureInfo.InvariantCulture,
                "Equity: {0:N2} {1}\nFree margin: {2:N2}\nAuto margin budget: {3:N2}\nRisk ceiling: {4:0.##}% equity",
                Account.Equity, Account.Asset.Name, freeMargin, autoBudget,
                AbsoluteRiskCeilingPercent > 0 ? AbsoluteRiskCeilingPercent : MaximumOpportunityRiskPercent);
        }

        private void SetVolumeNotice(string message, bool isWarning)
        {
            if (_volumeNoticeText == null)
                return;
            _volumeNoticeText.Text = message ?? string.Empty;
            _volumeNoticeText.ForegroundColor = isWarning ? Color.FromHex("#FFB4A8") : Color.FromHex("#B8C2D0");
        }
    }
}
