using System;
using System.Collections.Generic;
using UnityEngine;

public class ViewHandler : MonoBehaviour
{
    [SerializeField] private List<View> allViews = new();
    [SerializeField] private View defaultView;

    private void Start()
    {
        foreach (var view in allViews)
        {
            HideViewInternal(view);
        }
        ShowViewInternal(defaultView);
    }

    public View ShowView<T>(bool hideOthers = true) where T : View
    {
        View panel = null;
        foreach (var view in allViews)
        {
            if (!view)
                continue;
            if (view.GetType() == typeof(T))
            {
                ShowViewInternal(view);
            }
            else
            {
                if (hideOthers)
                    HideViewInternal(view);
            }

            panel = view;
        }

        return panel;
    }

    public void HideView<T>() where T : View
    {
        foreach (var view in allViews)
        {
            if (view.GetType() == typeof(T))
                HideViewInternal(view);
        }
    }

    private void ShowViewInternal(View view)
    {
        view.canvasGroup.alpha = 1;
        view.canvasGroup.interactable = true;
        view.canvasGroup.blocksRaycasts = true;
        view.OnShow();
        view.OnViewShow?.Invoke();
        view.gameObject.SetActive(true);
    }

    private void HideViewInternal(View view)
    {
        if (!view)
            return;

        if (view.canvasGroup)
        {
            view.canvasGroup.alpha = 0;
            view.canvasGroup.interactable = false;
            view.canvasGroup.blocksRaycasts = false;
        }

        view.OnHide();
        view.OnViewHide?.Invoke();

        view.gameObject.SetActive(false);
    }
}

[RequireComponent(typeof(CanvasGroup))]
public abstract class View : MonoBehaviour
{
    public CanvasGroup canvasGroup;

    public Action OnViewShow;
    public Action OnViewHide;

    public virtual void OnShow() {}
    public virtual void OnHide() {}
}