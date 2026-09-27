using System;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;
using TerminalOrganizer.Core.Assignment;
using TerminalOrganizer.Core.Overflow;

namespace TerminalOrganizer.App
{
    /// <summary>
    /// The injectable choice seam (C3): resolve an Ask policy to one concrete action.
    /// Production implementation marshals the modal dialog onto the UI thread and waits
    /// synchronously; the suite fakes this delegate directly (a blocked worker would
    /// freeze the Pester host — the seam exists so tests never touch a real form).
    /// </summary>
    public delegate OverflowChoiceResult OverflowChoicePrompt(PreparedOrganizeRun prepared, bool mergeEnabled);

    /// <summary>
    /// One renderable choice row (C3): the action, its label, and the
    /// visible/enabled/checked flags the dialog renders. Pure data; immutable.
    /// </summary>
    public sealed class OverflowChoiceOptionModel
    {
        private readonly OverflowChoice choice;
        private readonly string label;
        private readonly bool visible;
        private readonly bool enabled;
        private readonly bool checkedState;

        public OverflowChoiceOptionModel(OverflowChoice choice, string label, bool visible,
            bool enabled, bool checkedState)
        {
            this.choice = choice;
            this.label = label;
            this.visible = visible;
            this.enabled = enabled;
            this.checkedState = checkedState;
        }

        public OverflowChoice Choice { get { return choice; } }
        public string Label { get { return label; } }
        public bool Visible { get { return visible; } }
        public bool Enabled { get { return enabled; } }
        public bool Checked { get { return checkedState; } }
    }

    /// <summary>
    /// The choice dialog's whole payload (C3): the one-line summary (stacked / merge
    /// eligible / redistribution counts), the action rows, and the remember checkbox.
    /// Pure data — no WinForms types; the dialog renders it, the suite pins it.
    /// Immutable; arrays return copies.
    /// </summary>
    public sealed class OverflowChoiceDialogModel
    {
        private readonly string summaryText;
        private readonly OverflowChoiceOptionModel[] options;
        private readonly string rememberText;
        private readonly string cancelText;

        public OverflowChoiceDialogModel(string summaryText, OverflowChoiceOptionModel[] options,
            string rememberText, string cancelText)
        {
            this.summaryText = summaryText;
            this.options = options == null
                ? new OverflowChoiceOptionModel[0] : (OverflowChoiceOptionModel[])options.Clone();
            this.rememberText = rememberText;
            this.cancelText = cancelText;
        }

        public string SummaryText { get { return summaryText; } }
        public OverflowChoiceOptionModel[] Options { get { return (OverflowChoiceOptionModel[])options.Clone(); } }
        public string RememberText { get { return rememberText; } }
        public string CancelText { get { return cancelText; } }
    }

    /// <summary>
    /// Builds the dialog payload from one prepared run (C3, pure): the summary counts
    /// come from the plan — stacked moves in the local assignment, planned merges, and
    /// planned cross-monitor moves. The Merge row is always VISIBLE but enabled only
    /// while the merge release gate is open; no row starts checked.
    /// </summary>
    public static class OverflowChoiceModel
    {
        public static OverflowChoiceDialogModel Build(PreparedOrganizeRun prepared, bool mergeEnabled)
        {
            int stacked = 0;
            int merges = 0;
            int moves = 0;
            OrganizePlan plan = prepared == null ? null : prepared.Plan;
            if (plan != null)
            {
                stacked = CountStacked(plan.LocalAssignment);
                merges = plan.MergePlan == null ? 0 : plan.MergePlan.Merges.Length;
                moves = plan.RedistributionPlan == null ? 0 : plan.RedistributionPlan.Moves.Length;
            }
            string summary = string.Format(CultureInfo.InvariantCulture,
                "{0} stacked, {1} merge-eligible, {2} movable to other monitors", stacked, merges, moves);
            OverflowChoiceOptionModel[] options = new OverflowChoiceOptionModel[]
            {
                new OverflowChoiceOptionModel(OverflowChoice.Stack, "Stack only", true, true, false),
                new OverflowChoiceOptionModel(OverflowChoice.Redistribute,
                    "Move to free zones on other monitors", true, true, false),
                new OverflowChoiceOptionModel(OverflowChoice.Merge,
                    "Merge eligible tmux windows", true, mergeEnabled, false)
            };
            return new OverflowChoiceDialogModel(summary, options, "Remember this choice", "Cancel");
        }

        private static int CountStacked(AssignmentPlan plan)
        {
            int stacked = 0;
            if (plan == null)
            {
                return 0;
            }
            foreach (PlannedMove move in plan.Moves)
            {
                if (move != null && move.Stacked)
                {
                    stacked++;
                }
            }
            return stacked;
        }
    }

    /// <summary>
    /// The pre-mutation overflow choice dialog (C3; morning-checklist surface — the
    /// suite drives OverflowChoiceModel.Build, never this form). Shown ONLY after pure
    /// planning proved overflow; the worker never calls Show directly — the marshalled
    /// prompt below puts it on the UI thread.
    /// </summary>
    // @MX:WARN: [AUTO] real WinForms modal dialog — morning checklist; not an acceptance claim.
    internal static class OverflowChoiceDialog
    {
        internal static OverflowChoiceResult Show(IWin32Window owner, PreparedOrganizeRun prepared, bool mergeEnabled)
        {
            return Show(owner, prepared, mergeEnabled, CancellationToken.None);
        }

        /// <summary>Cancellation while the dialog is pending closes it and resolves to Cancel.</summary>
        internal static OverflowChoiceResult Show(IWin32Window owner, PreparedOrganizeRun prepared,
            bool mergeEnabled, CancellationToken cancellationToken)
        {
            OverflowChoiceDialogModel model = OverflowChoiceModel.Build(prepared, mergeEnabled);
            OverflowChoiceResult[] result = new OverflowChoiceResult[] { null };
            using (Form dialog = new Form())
            using (System.Windows.Forms.Label summary = new System.Windows.Forms.Label())
            using (CheckBox remember = new CheckBox())
            using (Button cancel = new Button())
            {
                dialog.Text = "Overflow on this monitor";
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.StartPosition = FormStartPosition.CenterScreen;
                dialog.MinimizeBox = false;
                dialog.MaximizeBox = false;
                dialog.Width = 460;
                dialog.Height = 120 + model.Options.Length * 40;
                summary.Left = 12;
                summary.Top = 12;
                summary.Width = 424;
                summary.Text = model.SummaryText;
                dialog.Controls.Add(summary);
                int top = 44;
                foreach (OverflowChoiceOptionModel option in model.Options)
                {
                    Button action = new Button();
                    action.Left = 12;
                    action.Top = top;
                    action.Width = 424;
                    action.Height = 32;
                    action.Text = option.Label;
                    action.Visible = option.Visible;
                    action.Enabled = option.Enabled;
                    OverflowChoice chosen = option.Choice;
                    action.Click += delegate
                    {
                        result[0] = new OverflowChoiceResult(chosen, remember.Checked);
                        dialog.Close();
                    };
                    dialog.Controls.Add(action);
                    top += 40;
                }
                remember.Left = 12;
                remember.Top = top;
                remember.Width = 240;
                remember.Text = model.RememberText;
                dialog.Controls.Add(remember);
                cancel.Left = 356;
                cancel.Top = top;
                cancel.Width = 80;
                cancel.Text = model.CancelText;
                cancel.DialogResult = DialogResult.Cancel;
                dialog.Controls.Add(cancel);
                dialog.CancelButton = cancel;
                // Cancellation while pending: Close from the registration thread must be
                // marshalled back onto the dialog's own thread.
                CancellationTokenRegistration registration = cancellationToken.Register(delegate
                {
                    try
                    {
                        dialog.BeginInvoke((Action)delegate { dialog.Close(); }, new object[0]);
                    }
                    catch (Exception)
                    {
                        // The dialog is already gone; the token path resolves to Cancel below.
                    }
                });
                try
                {
                    dialog.ShowDialog(owner);
                }
                finally
                {
                    registration.Dispose();
                }
            }
            if (cancellationToken.IsCancellationRequested)
            {
                return new OverflowChoiceResult(OverflowChoice.Cancel, false);
            }
            return result[0] == null
                ? new OverflowChoiceResult(OverflowChoice.Cancel, false)
                : result[0];
        }
    }

    /// <summary>
    /// The production choice seam (C3): the worker NEVER shows a form — the dialog runs
    /// on the UI thread through this synchronous marshal (BeginInvoke onto the B4
    /// marshalling control, then a ManualResetEvent wait). A token cancellation while
    /// the dialog is pending resolves to Cancel (the registration above closes the
    /// dialog best-effort) and the flow performs zero mutation.
    /// </summary>
    // @MX:WARN: [AUTO] worker thread blocks on the marshalling event (production only; the suite fakes the seam) + real dialog — morning checklist.
    internal sealed class MarshalledOverflowChoicePrompt
    {
        private readonly Control marshal;
        private readonly IWin32Window owner;

        /// <param name="marshal">The B4 marshalling control (handle must be created).</param>
        /// <param name="owner">The dialog owner window.</param>
        internal MarshalledOverflowChoicePrompt(Control marshal, IWin32Window owner)
        {
            if (marshal == null) throw new ArgumentNullException("marshal");
            this.marshal = marshal;
            this.owner = owner;
        }

        internal OverflowChoiceResult Prompt(PreparedOrganizeRun prepared, bool mergeEnabled,
            CancellationToken cancellationToken)
        {
            ManualResetEvent done = new ManualResetEvent(false);
            OverflowChoiceResult[] box = new OverflowChoiceResult[] { null };
            marshal.BeginInvoke((Action)delegate
            {
                try
                {
                    box[0] = OverflowChoiceDialog.Show(owner, prepared, mergeEnabled, cancellationToken);
                }
                catch (Exception)
                {
                    // A dialog failure is a cancel, never a worker crash.
                    box[0] = new OverflowChoiceResult(OverflowChoice.Cancel, false);
                }
                finally
                {
                    done.Set();
                }
            }, new object[0]);
            WaitHandle[] waits = new WaitHandle[] { done, cancellationToken.WaitHandle };
            bool completed = WaitHandle.WaitAny(waits) == 0;
            if (!completed || box[0] == null)
            {
                return new OverflowChoiceResult(OverflowChoice.Cancel, false);
            }
            return box[0];
        }
    }
}
