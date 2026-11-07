package com.mirrorsound;

import android.animation.ValueAnimator;
import android.content.Context;
import android.graphics.Canvas;
import android.graphics.LinearGradient;
import android.graphics.Paint;
import android.graphics.RadialGradient;
import android.graphics.RectF;
import android.graphics.Shader;
import android.view.View;
import android.view.animation.LinearInterpolator;

/** Big round start/stop button. While streaming it glows and sends out rings like sound waves. */
public class PulseButton extends View {
    private static final int VIOLET = 0xFF8B5CF6;
    private static final int BLUE = 0xFF3B82F6;

    private final Paint fill = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint idle = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint stroke = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint ring = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint glow = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint icon = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final RectF arc = new RectF();
    private final float density;

    private boolean active;
    private float phase;
    private ValueAnimator animator;

    public PulseButton(Context context) {
        super(context);
        density = getResources().getDisplayMetrics().density;
        setClickable(true);
        setFocusable(true);
        idle.setColor(0xFF1B1D27);
        stroke.setStyle(Paint.Style.STROKE);
        stroke.setStrokeWidth(2 * density);
        ring.setStyle(Paint.Style.STROKE);
        ring.setStrokeWidth(2.5f * density);
        ring.setColor(VIOLET);
        icon.setStyle(Paint.Style.STROKE);
        icon.setStrokeCap(Paint.Cap.ROUND);
        setContentDescription("Начать трансляцию");
    }

    public void setActive(boolean value) {
        if (active == value) return;
        active = value;
        setContentDescription(value ? "Остановить трансляцию" : "Начать трансляцию");
        if (value) {
            animator = ValueAnimator.ofFloat(0f, 1f);
            animator.setDuration(2600);
            animator.setRepeatCount(ValueAnimator.INFINITE);
            animator.setInterpolator(new LinearInterpolator());
            animator.addUpdateListener(a -> {
                phase = (float) a.getAnimatedValue();
                invalidate();
            });
            animator.start();
        } else if (animator != null) {
            animator.cancel();
            animator = null;
        }
        invalidate();
    }

    @Override
    protected void onSizeChanged(int w, int h, int oldw, int oldh) {
        float cx = w / 2f, cy = h / 2f, r = radius();
        fill.setShader(new LinearGradient(cx - r, cy - r, cx + r, cy + r, VIOLET, BLUE, Shader.TileMode.CLAMP));
        stroke.setShader(new LinearGradient(cx - r, cy - r, cx + r, cy + r, VIOLET, BLUE, Shader.TileMode.CLAMP));
        glow.setShader(new RadialGradient(cx, cy, r * 1.6f,
                new int[]{0x668B5CF6, 0x223B82F6, 0x003B82F6}, new float[]{0.45f, 0.75f, 1f}, Shader.TileMode.CLAMP));
    }

    private float radius() {
        return Math.min(getWidth(), getHeight()) / 2f * 0.6f;
    }

    @Override
    protected void onDraw(Canvas canvas) {
        float cx = getWidth() / 2f, cy = getHeight() / 2f, r = radius();
        float maxR = Math.min(getWidth(), getHeight()) / 2f - ring.getStrokeWidth();

        if (active) {
            canvas.drawCircle(cx, cy, r * 1.6f, glow);
            for (int k = 0; k < 3; k++) {
                float p = (phase + k / 3f) % 1f;
                ring.setAlpha((int) (150 * (1 - p)));
                canvas.drawCircle(cx, cy, r + p * (maxR - r), ring);
            }
            canvas.drawCircle(cx, cy, r, fill);
        } else {
            canvas.drawCircle(cx, cy, r, idle);
            canvas.drawCircle(cx, cy, r - stroke.getStrokeWidth() / 2, stroke);
        }

        // Power symbol
        icon.setColor(active ? 0xFFFFFFFF : 0xFFB9B2FF);
        icon.setStrokeWidth(r * 0.09f);
        float ir = r * 0.36f;
        arc.set(cx - ir, cy - ir + r * 0.04f, cx + ir, cy + ir + r * 0.04f);
        canvas.drawArc(arc, -60, 300, false, icon);
        canvas.drawLine(cx, cy - ir - r * 0.06f, cx, cy - r * 0.02f, icon);
    }

    @Override
    public void setPressed(boolean pressed) {
        super.setPressed(pressed);
        animate().scaleX(pressed ? 0.95f : 1f).scaleY(pressed ? 0.95f : 1f).setDuration(120).start();
    }

    @Override
    protected void onDetachedFromWindow() {
        super.onDetachedFromWindow();
        if (animator != null) animator.cancel();
    }
}
