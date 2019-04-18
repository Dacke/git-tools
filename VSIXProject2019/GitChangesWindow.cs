﻿namespace VSIXProject2019
{
    using System;
    using System.ComponentModel.Design;
    using System.Runtime.InteropServices;
    using System.Windows.Forms;
    using Microsoft;
    using Microsoft.VisualStudio;
    using Microsoft.VisualStudio.ComponentModelHost;
    using Microsoft.VisualStudio.Editor;
    using Microsoft.VisualStudio.OLE.Interop;
    using Microsoft.VisualStudio.ProjectSystem;
    using Microsoft.VisualStudio.Shell;
    using Microsoft.VisualStudio.Shell.Interop;
    using Microsoft.VisualStudio.Text;
    using Microsoft.VisualStudio.Text.Editor;
    using Microsoft.VisualStudio.TextManager.Interop;
    using IServiceProvider = Microsoft.VisualStudio.OLE.Interop.IServiceProvider;

    /// <summary>
    /// This class implements the tool window exposed by this package and hosts a user control.
    /// </summary>
    /// <remarks>
    /// In Visual Studio tool windows are composed of a frame (implemented by the shell) and a pane,
    /// usually implemented by the package implementer.
    /// <para>
    /// This class derives from the ToolWindowPane class provided from the MPF in order to use its
    /// implementation of the IVsUIElementPane interface.
    /// </para>
    /// </remarks>
    [Guid(WindowGuidString)]
    public class GitChangesWindow : ToolWindowPane //,IOleCommandTarget, IVsWindowFrameNotify3
    {
        public EnvDTE80.DTE2 DTE;

        IComponentModel _componentModel;
        IVsTextView _ViewAdapter;
        IVsTextBuffer _BufferAdapter;
        IVsEditorAdaptersFactoryService _EditorAdapterFactory;
        IServiceProvider _OleServiceProvider;
        ITextBufferFactoryService _BufferFactory;
        IWpfTextViewHost _TextViewHost;

        private IOleCommandTarget cachedEditorCommandTarget;
        private IVsTextView textView;
        private IVsCodeWindow codeWindow;
        private IVsInvisibleEditor invisibleEditor;
        private IVsFindTarget cachedEditorFindTarget;
        private Microsoft.VisualStudio.OLE.Interop.IServiceProvider cachedOleServiceProvider;


        public const string WindowGuidString = "e0487501-8bf2-4e94-8b35-ceb6f0010c44"; // Replace with new GUID in your own code
        public const string Title = "Git Changes Window";
        private GitChangesWindowControl _Control;


        /// <summary>
        /// Initializes a new instance of the <see cref="GitChangesWindow"/> class.
        /// </summary>
        public GitChangesWindow(GitChangesWindowState state) : base()
        {
            this.Caption = "Git Changes";
            this.ToolBar = new CommandID(GuidList.guidVsGitToolsPackageCmdSet, PkgCmdIDList.imnuGitChangesToolWindowToolbarMenu);
            // This is the user control hosted by the tool window; Note that, even if this class implements IDisposable,
            // we are not calling Dispose on this object. This is because ToolWindowPane calls Dispose on
            // the object returned by the Content property.
            this.Content = new GitChangesWindowControl(this);
            this.DTE = state.DTE;
        }

        protected override void Initialize()
        {
            base.Initialize();
            InitializeEditor();
        }

        override public object Content
        {
            get
            {
                if (_Control == null)
                {
                    _Control = new GitChangesWindowControl(this);
                    _Control.DiffEditor.Content = TextViewHost;
                }
                return _Control;
            }
        }


        internal IVsDifferenceService DiffService
        {
            get
            {
                return (IVsDifferenceService) this.GetService(typeof(SVsDifferenceService));
            }
        }


        #region vs editor
        private void InitializeEditor()
        {
            const string message = "";

            _componentModel = (IComponentModel)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(SComponentModel));
            _OleServiceProvider = (IServiceProvider)GetService(typeof(IServiceProvider));
            _BufferFactory = _componentModel.GetService<ITextBufferFactoryService>();
            _EditorAdapterFactory = _componentModel.GetService<IVsEditorAdaptersFactoryService>();
            _BufferAdapter = _EditorAdapterFactory.CreateVsTextBufferAdapter(_OleServiceProvider,_BufferFactory.TextContentType);
            _BufferAdapter.InitializeContent(message, message.Length);
            _ViewAdapter = _EditorAdapterFactory.CreateVsTextViewAdapter(_OleServiceProvider);
            ((IVsWindowPane)_ViewAdapter).SetSite(_OleServiceProvider);

            var initView = new[] { new INITVIEW() };
            initView[0].fSelectionMargin = 0; // original: 0
            initView[0].fWidgetMargin = 0; // original: 0
            initView[0].fVirtualSpace = 0;
            initView[0].fDragDropMove = 1;
            initView[0].fVirtualSpace = 0;

            _ViewAdapter.Initialize(_BufferAdapter as IVsTextLines, IntPtr.Zero,
              (uint)TextViewInitFlags.VIF_HSCROLL |
              (uint) 32768 /*TextViewInitFlags3.VIF_NO_HWND_SUPPORT*/, initView);
        }

        // ----------------------------------------------------------------------------------
        /// <summary>
        /// Gets the editor wpf host that we can use as the tool windows content.
        /// </summary>
        // ----------------------------------------------------------------------------------
        public IWpfTextViewHost TextViewHost
        {
            get
            {
                if (_TextViewHost == null)
                {
                    InitializeEditor();
                    var data = _ViewAdapter as IVsUserData;
                    if (data != null)
                    {
                        var guid = Microsoft.VisualStudio.Editor.DefGuidList.guidIWpfTextViewHost;
                        object obj;
                        var hr = data.GetData(ref guid, out obj);
                        if ((hr == Microsoft.VisualStudio.VSConstants.S_OK) &&
                            obj != null && obj is IWpfTextViewHost)
                        {
                            _TextViewHost = obj as IWpfTextViewHost;
                        }
                    }
                }
                return _TextViewHost;
            }
        }

        internal IVsTextView SetDisplayedFile(string filePath)
        {
            IVsInvisibleEditorManager invisibleEditorManager = (IVsInvisibleEditorManager)GetService(typeof(SVsInvisibleEditorManager));
            ErrorHandler.ThrowOnFailure(invisibleEditorManager.RegisterInvisibleEditor(filePath,
                                                                                       pProject: null,
                                                                                       dwFlags: (uint)_EDITORREGFLAGS.RIEF_ENABLECACHING,
                                                                                       pFactory: null,
                                                                                       ppEditor: out this.invisibleEditor));

            //The doc data is the IVsTextLines that represents the in-memory version of the file we opened in our invisibe editor, we need
            //to extract that so that we can create our real (visible) editor.
            IntPtr docDataPointer = IntPtr.Zero;
            Guid guidIVSTextLines = typeof(IVsTextLines).GUID;
            ErrorHandler.ThrowOnFailure(this.invisibleEditor.GetDocData(fEnsureWritable: 1, riid: ref guidIVSTextLines, ppDocData: out docDataPointer));
            try
            {
                IVsTextLines docData = (IVsTextLines)Marshal.GetObjectForIUnknown(docDataPointer);
                IVsEditorAdaptersFactoryService editorAdapterFactoryService = _componentModel.GetService<IVsEditorAdaptersFactoryService>();
                this.codeWindow = _EditorAdapterFactory.CreateVsCodeWindowAdapter(_OleServiceProvider);

                //Disable the splitter control on the editor as leaving it enabled causes a crash if the user
                //tries to use it here :(
                IVsCodeWindowEx codeWindowEx = (IVsCodeWindowEx)this.codeWindow;
                INITVIEW[] initView = new INITVIEW[1];
                codeWindowEx.Initialize((uint)_codewindowbehaviorflags.CWB_DISABLESPLITTER,
                                         VSUSERCONTEXTATTRIBUTEUSAGE.VSUC_Usage_Filter,
                                         szNameAuxUserContext: "",
                                         szValueAuxUserContext: "",
                                         InitViewFlags: 0,
                                         pInitView: initView);

                //docData.SetStateFlags((uint)BUFFERSTATEFLAGS.BSF_USER_READONLY); //set read only

                //Associate our IVsTextLines with our new code window.
                ErrorHandler.ThrowOnFailure(this.codeWindow.SetBuffer((IVsTextLines)docData));

                //Get our text view for our editor which we will use to get the WPF control that hosts said editor.
                ErrorHandler.ThrowOnFailure(this.codeWindow.GetPrimaryView(out this.textView));

                //Get our WPF host from our text view (from our code window).
                IWpfTextViewHost textViewHost = editorAdapterFactoryService.GetWpfTextViewHost(this.textView);

                //textViewHost.TextView.Options.SetOptionValue(GitTextViewOptions.DiffMarginId, false);
                textViewHost.TextView.Options.SetOptionValue(DefaultTextViewHostOptions.ChangeTrackingId, false);
                textViewHost.TextView.Options.SetOptionValue(DefaultTextViewHostOptions.GlyphMarginId, false);
                textViewHost.TextView.Options.SetOptionValue(DefaultTextViewHostOptions.LineNumberMarginId, false);
                textViewHost.TextView.Options.SetOptionValue(DefaultTextViewHostOptions.OutliningMarginId, false);
                textViewHost.TextView.Options.SetOptionValue(DefaultTextViewOptions.ViewProhibitUserInputId, true);
                return this.textView;
            }
            catch
            {
                return null;
            }
            finally
            {
                if (docDataPointer != IntPtr.Zero)
                {
                    //Release the doc data from the invisible editor since it gave us a ref-counted copy.
                    Marshal.Release(docDataPointer);
                }
            }
        }

        /// <summary>
        /// Cleans up an existing editor if we are about to put a new one in place, used to close down the old editor bits as well as
        /// nulling out any cached objects that we have that came from the now dead editor.
        /// </summary>
        internal void ClearEditor()
        {
            if (this.codeWindow != null)
            {
                this.codeWindow.Close();
                this.codeWindow = null;
            }

            if (this.textView != null)
            {
                this.textView.CloseView();
                this.textView = null;
            }

            this.cachedEditorCommandTarget = null;
            this.cachedEditorFindTarget = null;
            this.invisibleEditor = null;
        }

        #endregion

        #region process messages
        /*
                public override void OnToolWindowCreated()
                {
                    //We need to set up the tool window to respond to key bindings
                    //They're passed to the tool window and its buffers via Query() and Exec()
                    var windowFrame = (IVsWindowFrame)Frame;
                    var cmdUi = Microsoft.VisualStudio.VSConstants.GUID_TextEditorFactory;
                    windowFrame.SetGuidProperty((int)__VSFPROPID.VSFPROPID_InheritKeyBindings, ref cmdUi);
                    base.OnToolWindowCreated();
                }

                protected override bool PreProcessMessage(ref Message m)
                {
                    if (TextViewHost != null)
                    {
                        // copy the Message into a MSG[] array, so we can pass
                        // it along to the active core editor's IVsWindowPane.TranslateAccelerator
                        var pMsg = new MSG[1];
                        pMsg[0].hwnd = m.HWnd;
                        pMsg[0].message = (uint)m.Msg;
                        pMsg[0].wParam = m.WParam;
                        pMsg[0].lParam = m.LParam;

                        var vsWindowPane = (IVsWindowPane)textView;
                        return vsWindowPane.TranslateAccelerator(pMsg) == 0;
                    }
                    return base.PreProcessMessage(ref m);
                }

                int IOleCommandTarget.Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
                {
                    var hr = (int) Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_NOTSUPPORTED;

                    if (textView != null)
                    {
                        var cmdTarget = (IOleCommandTarget)textView;
                        hr = cmdTarget.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
                    }
                    return hr;
                }

                int IOleCommandTarget.QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
                {
                    var hr = (int) Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_NOTSUPPORTED;
                    if (textView != null)
                    {
                        var cmdTarget = (IOleCommandTarget)textView;
                        hr = cmdTarget.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText);
                    }
                    return hr;
                }
        */
        #endregion

        #region IVsWindowFrameNotify3
        /*
                public int OnClose(ref uint pgrfSaveOptions)
                {
                    return Microsoft.VisualStudio.VSConstants.S_OK;
                }

                public int OnDockableChange(int fDockable, int x, int y, int w, int h)
                {
                    return Microsoft.VisualStudio.VSConstants.S_OK;
                }

                public int OnMove(int x, int y, int w, int h)
                {
                    return Microsoft.VisualStudio.VSConstants.S_OK;
                }

                public int OnShow(int fShow)
                {
                    if (fShow == (int)__FRAMESHOW.FRAMESHOW_WinShown)
                    {
                        var svc = this.GetService(typeof(IVsUIShell)) as IVsUIShell;
                        svc.UpdateCommandUI(1);

                        _Control.ReloadEditor();
                    }
                    return Microsoft.VisualStudio.VSConstants.S_OK;
                }

                public int OnSize(int x, int y, int w, int h)
                {
                    return Microsoft.VisualStudio.VSConstants.S_OK;
                }
        */
        #endregion

    }


    public class GitChangesWindowState
    {
        public EnvDTE80.DTE2 DTE { get; set; }
    }
}
