#include "EasyTryE57Native.h"

#include <algorithm>
#include <array>
#include <exception>
#include <limits>
#include <memory>
#include <string>
#include <vector>

#include <E57Format.h>

namespace
{
   constexpr const char *kEasyTryPrefix = "easytry";
   constexpr const char *kEasyTryNamespace = "urn:easytry:e57:1.0";

   enum FieldMask : std::uint32_t
   {
      CartesianX = 1U << 0U,
      CartesianY = 1U << 1U,
      CartesianZ = 1U << 2U,
      Intensity = 1U << 3U,
      RawIntensity = 1U << 4U,
      RowIndex = 1U << 5U,
      ColumnIndex = 1U << 6U
   };

   thread_local std::string g_lastError;

   void setLastError( const std::exception &exception )
   {
      g_lastError = exception.what();
   }

   void setLastError( const e57::E57Exception &exception )
   {
      g_lastError = exception.errorStr();
      if ( !exception.context().empty() )
      {
         g_lastError += "; ";
         g_lastError += exception.context();
      }
   }

   void setLastError( const char *message )
   {
      g_lastError = message == nullptr ? "Unknown native E57 error." : message;
   }

   struct WriterContext
   {
      WriterContext( const char *filePath, const std::size_t bufferCapacity ) :
         image( std::make_unique<e57::ImageFile>( filePath, "w" ) ),
         x( bufferCapacity ),
         y( bufferCapacity ),
         z( bufferCapacity ),
         processedIntensity( bufferCapacity ),
         rawIntensity( bufferCapacity ),
         rowIndex( bufferCapacity ),
         columnIndex( bufferCapacity )
      {
      }

      std::unique_ptr<e57::ImageFile> image;
      std::unique_ptr<e57::CompressedVectorWriter> writer;
      std::vector<double> x;
      std::vector<double> y;
      std::vector<double> z;
      std::vector<float> processedIntensity;
      std::vector<double> rawIntensity;
      std::vector<std::uint32_t> rowIndex;
      std::vector<std::uint32_t> columnIndex;
      bool completed = false;
   };

   struct ReaderContext
   {
      ReaderContext( const char *filePath, const std::size_t bufferCapacity ) :
         image( std::make_unique<e57::ImageFile>( filePath, "r", e57::ChecksumNone ) ),
         x( bufferCapacity ),
         y( bufferCapacity ),
         z( bufferCapacity ),
         processedIntensity( bufferCapacity ),
         rawIntensity( bufferCapacity ),
         rowIndex( bufferCapacity ),
         columnIndex( bufferCapacity ),
         capacity( bufferCapacity )
      {
      }

      std::unique_ptr<e57::ImageFile> image;
      std::unique_ptr<e57::CompressedVectorReader> reader;
      std::vector<double> x;
      std::vector<double> y;
      std::vector<double> z;
      std::vector<float> processedIntensity;
      std::vector<double> rawIntensity;
      std::vector<std::uint32_t> rowIndex;
      std::vector<std::uint32_t> columnIndex;
      std::size_t capacity;
   };

   std::vector<e57::SourceDestBuffer> makeBuffers(
      const e57::ImageFile &image,
      double *x,
      double *y,
      double *z,
      float *processedIntensity,
      double *rawIntensity,
      std::uint32_t *rowIndex,
      std::uint32_t *columnIndex,
      const std::size_t pointCount )
   {
      std::vector<e57::SourceDestBuffer> buffers;
      buffers.reserve( 7 );
      buffers.emplace_back( image, "cartesianX", x, pointCount );
      buffers.emplace_back( image, "cartesianY", y, pointCount );
      buffers.emplace_back( image, "cartesianZ", z, pointCount );
      buffers.emplace_back( image, "intensity", processedIntensity, pointCount );
      buffers.emplace_back( image, "easytry:rawIntensity", rawIntensity, pointCount );
      buffers.emplace_back( image, "rowIndex", rowIndex, pointCount );
      buffers.emplace_back( image, "columnIndex", columnIndex, pointCount );
      return buffers;
   }

   std::uint32_t getFieldMask( const e57::StructureNode &prototype )
   {
      std::uint32_t mask = 0;
      mask |= prototype.isDefined( "cartesianX" ) ? CartesianX : 0;
      mask |= prototype.isDefined( "cartesianY" ) ? CartesianY : 0;
      mask |= prototype.isDefined( "cartesianZ" ) ? CartesianZ : 0;
      mask |= prototype.isDefined( "intensity" ) ? Intensity : 0;
      mask |= prototype.isDefined( "easytry:rawIntensity" ) ? RawIntensity : 0;
      mask |= prototype.isDefined( "rowIndex" ) ? RowIndex : 0;
      mask |= prototype.isDefined( "columnIndex" ) ? ColumnIndex : 0;
      return mask;
   }

   void cancelNoThrow( WriterContext *context )
   {
      if ( context == nullptr || context->completed )
      {
         return;
      }

      try
      {
         if ( context->writer && context->writer->isOpen() )
         {
            context->writer->close();
         }
      }
      catch ( ... )
      {
      }

      try
      {
         if ( context->image && context->image->isOpen() )
         {
            context->image->cancel();
         }
      }
      catch ( ... )
      {
      }
   }

   void closeReaderNoThrow( ReaderContext *context )
   {
      if ( context == nullptr )
      {
         return;
      }

      try
      {
         if ( context->reader && context->reader->isOpen() )
         {
            context->reader->close();
         }
      }
      catch ( ... )
      {
      }

      try
      {
         if ( context->image && context->image->isOpen() )
         {
            context->image->close();
         }
      }
      catch ( ... )
      {
      }
   }

   template <typename Function>
   int runChecked( Function &&function )
   {
      g_lastError.clear();
      try
      {
         function();
         return 1;
      }
      catch ( const e57::E57Exception &exception )
      {
         setLastError( exception );
         return 0;
      }
      catch ( const std::exception &exception )
      {
         setLastError( exception );
         return 0;
      }
      catch ( ... )
      {
         setLastError( "Unknown native E57 error." );
         return 0;
      }
   }
}

void *EASYTRY_E57_CALL easytry_e57_create(
   const char *filePath,
   const char *fileGuid,
   const char *scanGuid,
   const char *scanName,
   const char *description,
   const std::uint32_t scanRows,
   const std::uint32_t scanColumns,
   const std::size_t bufferCapacity )
{
   g_lastError.clear();
   try
   {
      if ( filePath == nullptr || fileGuid == nullptr || scanGuid == nullptr ||
           scanRows == 0 || scanColumns == 0 || bufferCapacity == 0 )
      {
         throw std::invalid_argument( "Invalid E57 writer creation arguments." );
      }

      auto context = std::make_unique<WriterContext>( filePath, bufferCapacity );
      auto &image = *context->image;
      image.extensionsAdd( kEasyTryPrefix, kEasyTryNamespace );

      auto root = image.root();
      root.set( "formatName", e57::StringNode( image, "ASTM E57 3D Imaging Data File" ) );
      root.set( "guid", e57::StringNode( image, fileGuid ) );
      root.set( "versionMajor", e57::IntegerNode( image, 1 ) );
      root.set( "versionMinor", e57::IntegerNode( image, 0 ) );

      e57::VectorNode data3D( image, true );
      root.set( "data3D", data3D );

      e57::StructureNode scan( image );
      scan.set( "guid", e57::StringNode( image, scanGuid ) );
      if ( scanName != nullptr && scanName[0] != '\0' )
      {
         scan.set( "name", e57::StringNode( image, scanName ) );
      }
      if ( description != nullptr && description[0] != '\0' )
      {
         scan.set( "description", e57::StringNode( image, description ) );
      }

      e57::StructureNode indexBounds( image );
      indexBounds.set( "rowMinimum", e57::IntegerNode( image, 0 ) );
      indexBounds.set( "rowMaximum", e57::IntegerNode( image, scanRows - 1U ) );
      indexBounds.set( "columnMinimum", e57::IntegerNode( image, 0 ) );
      indexBounds.set( "columnMaximum", e57::IntegerNode( image, scanColumns - 1U ) );
      scan.set( "indexBounds", indexBounds );

      e57::StructureNode intensityLimits( image );
      intensityLimits.set(
         "intensityMinimum",
         e57::FloatNode( image, 0.0, e57::PrecisionSingle, 0.0, 1.0 ) );
      intensityLimits.set(
         "intensityMaximum",
         e57::FloatNode( image, 1.0, e57::PrecisionSingle, 0.0, 1.0 ) );
      scan.set( "intensityLimits", intensityLimits );

      e57::StructureNode prototype( image );
      prototype.set( "cartesianX", e57::FloatNode( image ) );
      prototype.set( "cartesianY", e57::FloatNode( image ) );
      prototype.set( "cartesianZ", e57::FloatNode( image ) );
      prototype.set(
         "intensity",
         e57::FloatNode( image, 0.0, e57::PrecisionSingle, 0.0, 1.0 ) );
      prototype.set( "easytry:rawIntensity", e57::FloatNode( image ) );
      prototype.set(
         "rowIndex",
         e57::IntegerNode( image, 0, 0, static_cast<std::int64_t>( scanRows ) - 1 ) );
      prototype.set(
         "columnIndex",
         e57::IntegerNode( image, 0, 0, static_cast<std::int64_t>( scanColumns ) - 1 ) );

      e57::VectorNode codecs( image, true );
      e57::CompressedVectorNode points( image, prototype, codecs );
      scan.set( "points", points );
      data3D.append( scan );

      auto initialBuffers = makeBuffers(
         image,
         context->x.data(),
         context->y.data(),
         context->z.data(),
         context->processedIntensity.data(),
         context->rawIntensity.data(),
         context->rowIndex.data(),
         context->columnIndex.data(),
         bufferCapacity );
      context->writer =
         std::make_unique<e57::CompressedVectorWriter>( points.writer( initialBuffers ) );
      return context.release();
   }
   catch ( const e57::E57Exception &exception )
   {
      setLastError( exception );
      return nullptr;
   }
   catch ( const std::exception &exception )
   {
      setLastError( exception );
      return nullptr;
   }
   catch ( ... )
   {
      setLastError( "Unknown native E57 creation error." );
      return nullptr;
   }
}

int EASYTRY_E57_CALL easytry_e57_write(
   void *handle,
   const double *x,
   const double *y,
   const double *z,
   const float *processedIntensity,
   const double *rawIntensity,
   const std::uint32_t *rowIndex,
   const std::uint32_t *columnIndex,
   const std::size_t pointCount )
{
   return runChecked( [&]() {
      auto *context = static_cast<WriterContext *>( handle );
      if ( context == nullptr || context->completed || !context->writer )
      {
         throw std::logic_error( "E57 writer is not open." );
      }
      if ( pointCount == 0 )
      {
         return;
      }
      if ( x == nullptr || y == nullptr || z == nullptr || processedIntensity == nullptr ||
           rawIntensity == nullptr || rowIndex == nullptr || columnIndex == nullptr )
      {
         throw std::invalid_argument( "A point buffer is null." );
      }

      if ( pointCount > context->x.size() )
      {
         throw std::length_error( "The E57 point batch exceeds the fixed native buffer capacity." );
      }

      std::copy_n( x, pointCount, context->x.begin() );
      std::copy_n( y, pointCount, context->y.begin() );
      std::copy_n( z, pointCount, context->z.begin() );
      std::copy_n( processedIntensity, pointCount, context->processedIntensity.begin() );
      std::copy_n( rawIntensity, pointCount, context->rawIntensity.begin() );
      std::copy_n( rowIndex, pointCount, context->rowIndex.begin() );
      std::copy_n( columnIndex, pointCount, context->columnIndex.begin() );
      context->writer->write( pointCount );
   } );
}

int EASYTRY_E57_CALL easytry_e57_complete( void *handle )
{
   return runChecked( [&]() {
      auto *context = static_cast<WriterContext *>( handle );
      if ( context == nullptr || context->completed )
      {
         throw std::logic_error( "E57 writer is not open." );
      }
      context->writer->close();
      context->image->close();
      context->completed = true;
   } );
}

void EASYTRY_E57_CALL easytry_e57_cancel( void *handle )
{
   g_lastError.clear();
   cancelNoThrow( static_cast<WriterContext *>( handle ) );
}

void EASYTRY_E57_CALL easytry_e57_destroy( void *handle )
{
   auto *context = static_cast<WriterContext *>( handle );
   cancelNoThrow( context );
   delete context;
}

int EASYTRY_E57_CALL easytry_e57_validate(
   const char *filePath,
   const int readAllPoints,
   std::uint64_t *pointCount,
   std::uint32_t *fieldMask )
{
   return runChecked( [&]() {
      if ( filePath == nullptr || pointCount == nullptr || fieldMask == nullptr )
      {
         throw std::invalid_argument( "Invalid E57 validation arguments." );
      }

      e57::ImageFile image( filePath, "r", e57::ChecksumAll );
      auto root = image.root();
      e57::VectorNode data3D( root.get( "data3D" ) );
      if ( data3D.childCount() != 1 )
      {
         throw std::runtime_error( "The E57 file must contain exactly one data3D scan." );
      }

      e57::StructureNode scan( data3D.get( 0 ) );
      e57::CompressedVectorNode points( scan.get( "points" ) );
      e57::StructureNode prototype( points.prototype() );

      *pointCount = static_cast<std::uint64_t>( points.childCount() );
      *fieldMask = getFieldMask( prototype );

      if ( readAllPoints != 0 && *pointCount > 0 )
      {
         constexpr std::size_t capacity = 65536;
         std::vector<double> x( capacity );
         std::vector<double> y( capacity );
         std::vector<double> z( capacity );
         std::vector<float> intensity( capacity );
         std::vector<double> rawIntensity( capacity );
         std::vector<std::uint32_t> rowIndex( capacity );
         std::vector<std::uint32_t> columnIndex( capacity );
         auto buffers = makeBuffers(
            image,
            x.data(),
            y.data(),
            z.data(),
            intensity.data(),
            rawIntensity.data(),
            rowIndex.data(),
            columnIndex.data(),
            capacity );
         auto reader = points.reader( buffers );
         std::uint64_t readCount = 0;
         while ( const auto count = reader.read() )
         {
            readCount += count;
         }
         reader.close();
         if ( readCount != *pointCount )
         {
            throw std::runtime_error( "E57 point count changed during full readback." );
         }
      }

      image.close();
   } );
}

void *EASYTRY_E57_CALL easytry_e57_reader_open(
   const char *filePath,
   const std::size_t bufferCapacity,
   std::uint32_t *scanRows,
   std::uint32_t *scanColumns,
   std::uint64_t *pointCount,
   std::uint32_t *fieldMask )
{
   g_lastError.clear();
   try
   {
      if ( filePath == nullptr || bufferCapacity == 0 || scanRows == nullptr ||
           scanColumns == nullptr || pointCount == nullptr || fieldMask == nullptr )
      {
         throw std::invalid_argument( "Invalid E57 reader creation arguments." );
      }

      g_lastError = "Opening E57 ImageFile.";
      auto context = std::make_unique<ReaderContext>( filePath, bufferCapacity );
      auto &image = *context->image;
      g_lastError = "Reading E57 root node.";
      auto root = image.root();
      g_lastError = "Reading E57 data3D vector.";
      e57::VectorNode data3D( root.get( "data3D" ) );
      if ( data3D.childCount() != 1 )
      {
         throw std::runtime_error( "The E57 file must contain exactly one data3D scan." );
      }

      g_lastError = "Reading E57 scan structure.";
      e57::StructureNode scan( data3D.get( 0 ) );
      g_lastError = "Reading E57 index bounds.";
      e57::StructureNode indexBounds( scan.get( "indexBounds" ) );
      const auto rowMinimum = e57::IntegerNode( indexBounds.get( "rowMinimum" ) ).value();
      const auto rowMaximum = e57::IntegerNode( indexBounds.get( "rowMaximum" ) ).value();
      const auto columnMinimum =
         e57::IntegerNode( indexBounds.get( "columnMinimum" ) ).value();
      const auto columnMaximum =
         e57::IntegerNode( indexBounds.get( "columnMaximum" ) ).value();
      if ( rowMinimum != 0 || columnMinimum != 0 || rowMaximum < rowMinimum ||
           columnMaximum < columnMinimum )
      {
         throw std::runtime_error( "Unsupported E57 row or column index bounds." );
      }

      g_lastError = "Reading E57 point prototype.";
      e57::CompressedVectorNode points( scan.get( "points" ) );
      e57::StructureNode prototype( points.prototype() );
      const auto mask = getFieldMask( prototype );
      constexpr std::uint32_t requiredMask =
         CartesianX | CartesianY | CartesianZ | Intensity | RawIntensity | RowIndex |
         ColumnIndex;
      if ( mask != requiredMask )
      {
         throw std::runtime_error( "The E57 point prototype is missing required fields." );
      }

      *scanRows = static_cast<std::uint32_t>( rowMaximum - rowMinimum + 1 );
      *scanColumns = static_cast<std::uint32_t>( columnMaximum - columnMinimum + 1 );
      *pointCount = static_cast<std::uint64_t>( points.childCount() );
      *fieldMask = mask;

      g_lastError = "Creating E57 point buffers.";
      auto buffers = makeBuffers(
         image,
         context->x.data(),
         context->y.data(),
         context->z.data(),
         context->processedIntensity.data(),
         context->rawIntensity.data(),
         context->rowIndex.data(),
         context->columnIndex.data(),
         bufferCapacity );
      g_lastError = "Creating E57 compressed-vector reader.";
      context->reader =
         std::make_unique<e57::CompressedVectorReader>( points.reader( buffers ) );
      g_lastError.clear();
      return context.release();
   }
   catch ( const e57::E57Exception &exception )
   {
      setLastError( exception );
      return nullptr;
   }
   catch ( const std::exception &exception )
   {
      setLastError( exception );
      return nullptr;
   }
   catch ( ... )
   {
      setLastError( "Unknown native E57 reader error." );
      return nullptr;
   }
}

int EASYTRY_E57_CALL easytry_e57_reader_read(
   void *handle,
   double *x,
   double *y,
   double *z,
   float *processedIntensity,
   double *rawIntensity,
   std::uint32_t *rowIndex,
   std::uint32_t *columnIndex,
   const std::size_t destinationCapacity,
   std::size_t *pointsRead )
{
   return runChecked( [&]() {
      auto *context = static_cast<ReaderContext *>( handle );
      if ( context == nullptr || context->reader == nullptr || pointsRead == nullptr )
      {
         throw std::invalid_argument( "Invalid E57 reader handle or output count." );
      }
      if ( x == nullptr || y == nullptr || z == nullptr || processedIntensity == nullptr ||
           rawIntensity == nullptr || rowIndex == nullptr || columnIndex == nullptr )
      {
         throw std::invalid_argument( "An E57 reader destination buffer is null." );
      }
      if ( destinationCapacity < context->capacity )
      {
         throw std::invalid_argument( "The E57 reader destination capacity is too small." );
      }

      const auto count = context->reader->read();
      *pointsRead = count;
      if ( count == 0 )
      {
         return;
      }

      std::copy_n( context->x.data(), count, x );
      std::copy_n( context->y.data(), count, y );
      std::copy_n( context->z.data(), count, z );
      std::copy_n( context->processedIntensity.data(), count, processedIntensity );
      std::copy_n( context->rawIntensity.data(), count, rawIntensity );
      std::copy_n( context->rowIndex.data(), count, rowIndex );
      std::copy_n( context->columnIndex.data(), count, columnIndex );
   } );
}

void EASYTRY_E57_CALL easytry_e57_reader_destroy( void *handle )
{
   auto *context = static_cast<ReaderContext *>( handle );
   closeReaderNoThrow( context );
   delete context;
}

const char *EASYTRY_E57_CALL easytry_e57_last_error()
{
   return g_lastError.c_str();
}
