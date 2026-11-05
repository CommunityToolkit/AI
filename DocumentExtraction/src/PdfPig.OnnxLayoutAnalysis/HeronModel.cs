// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis;

internal static class HeronModel
{
    private static readonly string[] s_labels =
    [
        "caption", "footnote", "formula", "list_item", "page_footer", "page_header",
        "picture", "section_header", "table", "text", "title", "document_index",
        "code", "checkbox_selected", "checkbox_unselected", "form", "key_value_region",
    ];

    internal static void Validate(InferenceSession session)
    {
        Check(session.InputMetadata, "images", typeof(byte), [-1, 3, 640, 640]);
        Check(session.InputMetadata, "orig_target_sizes", typeof(long), [-1, 2]);
        Check(session.OutputMetadata, "labels", typeof(long), [-1, 300]);
        Check(session.OutputMetadata, "boxes", typeof(float), [-1, 300, 4]);
        Check(session.OutputMetadata, "scores", typeof(float), [-1, 300]);
        if (session.InputMetadata.Count != 2 || session.OutputMetadata.Count != 3)
        {
            throw new ArgumentException("Expected the Docling Heron uint8 ONNX export with two inputs and three outputs.", nameof(session));
        }
    }

    internal static IReadOnlyList<HeronDetection> Detect(
        InferenceSession session, PageRaster raster, float threshold, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        DenseTensor<byte> images = raster.ToTensor(token);
        var sizes = new DenseTensor<long>(new long[] { raster.Height, raster.Width }, new[] { 1, 2 });
        var inputs = new[]
        {
            NamedOnnxValue.CreateFromTensor("images", images),
            NamedOnnxValue.CreateFromTensor("orig_target_sizes", sizes),
        };
        using var runOptions = new RunOptions();
        using var registration = token.Register(() => runOptions.Terminate = true);
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results;
        try
        {
            results = session.Run(inputs, session.OutputNames, runOptions);
        }
        catch (OnnxRuntimeException) when (token.IsCancellationRequested)
        {
            throw new OperationCanceledException(token);
        }

        using (results)
        {
            token.ThrowIfCancellationRequested();
            Tensor<long> labels = results.Single(result => result.Name == "labels").AsTensor<long>();
            Tensor<float> boxes = results.Single(result => result.Name == "boxes").AsTensor<float>();
            Tensor<float> scores = results.Single(result => result.Name == "scores").AsTensor<float>();
            if (!labels.Dimensions.SequenceEqual(new[] { 1, 300 }) ||
                !boxes.Dimensions.SequenceEqual(new[] { 1, 300, 4 }) ||
                !scores.Dimensions.SequenceEqual(new[] { 1, 300 }))
            {
                throw new InvalidDataException("Heron returned unexpected batch/output dimensions.");
            }

            var detections = new List<HeronDetection>();
            for (int i = 0; i < 300; i++)
            {
                token.ThrowIfCancellationRequested();
                float confidence = scores[0, i];
                if (!float.IsFinite(confidence) || confidence is < 0 or > 1)
                {
                    throw new InvalidDataException("Heron returned invalid confidence.");
                }

                if (confidence < threshold)
                {
                    continue;
                }

                long label = labels[0, i];
                float left = boxes[0, i, 0], top = boxes[0, i, 1];
                float right = boxes[0, i, 2], bottom = boxes[0, i, 3];
                if (label < 0 || label >= s_labels.Length ||
                    !float.IsFinite(left) || !float.IsFinite(top) || !float.IsFinite(right) || !float.IsFinite(bottom) ||
                    left >= right || top >= bottom)
                {
                    throw new InvalidDataException("Heron returned an invalid label or region.");
                }

                var bounds = raster.ToPageBounds(left, top, right, bottom);
                if (bounds.Width > 0 && bounds.Height > 0)
                {
                    detections.Add(new(s_labels[label], confidence, bounds));
                }
            }

            return detections.OrderByDescending(d => d.Bounds.Top).ThenBy(d => d.Bounds.Left).ToArray();
        }
    }

    private static void Check(IReadOnlyDictionary<string, NodeMetadata> metadata, string name, Type type, int[] shape)
    {
        if (!metadata.TryGetValue(name, out var node) || !node.IsTensor || node.ElementType != type ||
            node.Dimensions.Length != shape.Length ||
            node.Dimensions.Where((dimension, index) => shape[index] == -1
                ? dimension is not (-1 or 1) : dimension != shape[index]).Any())
        {
            throw new ArgumentException($"Incompatible Heron tensor '{name}'; expected {type.Name} [{string.Join(", ", shape)}].");
        }
    }
}
