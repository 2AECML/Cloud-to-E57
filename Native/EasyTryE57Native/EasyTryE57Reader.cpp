#include <algorithm>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <exception>
#include <iostream>
#include <limits>
#include <string>
#include <vector>

#ifdef _WIN32
#  ifndef NOMINMAX
#    define NOMINMAX
#  endif
#  include <fcntl.h>
#  include <io.h>
#  include <windows.h>
#endif

#include <E57Format.h>

namespace
{
   constexpr std::uint32_t kRequiredFieldMask = 0x7FU;
   constexpr std::uint32_t kPreviewFieldMask =
      ( 1U << 4U ) | ( 1U << 5U ) | ( 1U << 6U );
   constexpr char kProtocolMagic[8] = {'E', 'T', 'R', 'D', 'R', '0', '0', '1'};

#pragma pack(push, 1)
   struct StreamHeader
   {
      char magic[8];
      std::uint32_t scanRows;
      std::uint32_t scanColumns;
      std::uint64_t pointCount;
      std::uint32_t fieldMask;
   };
#pragma pack(pop)

   std::string toUtf8( const wchar_t *value )
   {
#ifdef _WIN32
      if ( value == nullptr )
      {
         return {};
      }
      const int length = WideCharToMultiByte(
         CP_UTF8, 0, value, -1, nullptr, 0, nullptr, nullptr );
      if ( length <= 1 )
      {
         return {};
      }
      std::string result( static_cast<std::size_t>( length ), '\0' );
      WideCharToMultiByte(
         CP_UTF8, 0, value, -1, result.data(), length, nullptr, nullptr );
      result.pop_back();
      return result;
#else
      return {};
#endif
   }

   std::uint32_t fieldMask( const e57::StructureNode &prototype )
   {
      std::uint32_t result = 0;
      result |= prototype.isDefined( "cartesianX" ) ? 1U << 0U : 0;
      result |= prototype.isDefined( "cartesianY" ) ? 1U << 1U : 0;
      result |= prototype.isDefined( "cartesianZ" ) ? 1U << 2U : 0;
      result |= prototype.isDefined( "intensity" ) ? 1U << 3U : 0;
      result |= prototype.isDefined( "easytry:rawIntensity" ) ? 1U << 4U : 0;
      result |= prototype.isDefined( "rowIndex" ) ? 1U << 5U : 0;
      result |= prototype.isDefined( "columnIndex" ) ? 1U << 6U : 0;
      return result;
   }

   template <typename T>
   void writeValue( const T &value )
   {
      std::cout.write(
         reinterpret_cast<const char *>( &value ),
         static_cast<std::streamsize>( sizeof( T ) ) );
      if ( !std::cout )
      {
         throw std::runtime_error( "Failed to write E57 reader protocol output." );
      }
   }

   template <typename T>
   void writeArray( const std::vector<T> &values, const std::size_t count )
   {
      std::cout.write(
         reinterpret_cast<const char *>( values.data() ),
         static_cast<std::streamsize>( count * sizeof( T ) ) );
      if ( !std::cout )
      {
         throw std::runtime_error( "Failed to write an E57 point batch." );
      }
   }

   int run(
      const std::string &filePath,
      const std::size_t capacity,
      const bool previewOnly )
   {
      if ( filePath.empty() || capacity == 0 )
      {
         throw std::invalid_argument( "A file path and positive buffer capacity are required." );
      }

#ifdef _WIN32
      if ( _setmode( _fileno( stdout ), _O_BINARY ) == -1 )
      {
         throw std::runtime_error( "Unable to switch stdout to binary mode." );
      }
#endif

      e57::ImageFile image( filePath, "r", e57::ChecksumNone );
      auto root = image.root();
      e57::VectorNode data3D( root.get( "data3D" ) );
      if ( data3D.childCount() != 1 )
      {
         throw std::runtime_error( "The E57 file must contain exactly one data3D scan." );
      }

      e57::StructureNode scan( data3D.get( 0 ) );
      e57::StructureNode bounds( scan.get( "indexBounds" ) );
      const auto rowMinimum = e57::IntegerNode( bounds.get( "rowMinimum" ) ).value();
      const auto rowMaximum = e57::IntegerNode( bounds.get( "rowMaximum" ) ).value();
      const auto columnMinimum =
         e57::IntegerNode( bounds.get( "columnMinimum" ) ).value();
      const auto columnMaximum =
         e57::IntegerNode( bounds.get( "columnMaximum" ) ).value();
      if ( rowMinimum != 0 || columnMinimum != 0 || rowMaximum < rowMinimum ||
           columnMaximum < columnMinimum )
      {
         throw std::runtime_error( "Unsupported E57 row or column bounds." );
      }
      if ( rowMaximum >= std::numeric_limits<std::uint32_t>::max() ||
           columnMaximum >= std::numeric_limits<std::uint32_t>::max() )
      {
         throw std::runtime_error( "E57 row or column bounds exceed uint32." );
      }

      e57::CompressedVectorNode points( scan.get( "points" ) );
      e57::StructureNode prototype( points.prototype() );
      const auto mask = fieldMask( prototype );
      if ( mask != kRequiredFieldMask )
      {
         throw std::runtime_error( "The E57 point prototype is missing required fields." );
      }

      const auto outputMask = previewOnly ? kPreviewFieldMask : kRequiredFieldMask;
      StreamHeader header{
         {},
         static_cast<std::uint32_t>( rowMaximum + 1 ),
         static_cast<std::uint32_t>( columnMaximum + 1 ),
         static_cast<std::uint64_t>( points.childCount() ),
         outputMask};
      std::copy( std::begin( kProtocolMagic ), std::end( kProtocolMagic ), header.magic );
      writeValue( header );
      std::cout.flush();

      std::vector<double> rawIntensity( capacity );
      std::vector<std::uint32_t> rowIndex( capacity );
      std::vector<std::uint32_t> columnIndex( capacity );
      std::uint64_t totalRead = 0;
      if ( previewOnly )
      {
         std::vector<e57::SourceDestBuffer> buffers;
         buffers.reserve( 3 );
         buffers.emplace_back(
            image, "easytry:rawIntensity", rawIntensity.data(), capacity );
         buffers.emplace_back( image, "rowIndex", rowIndex.data(), capacity );
         buffers.emplace_back( image, "columnIndex", columnIndex.data(), capacity );
         auto reader = points.reader( buffers );
         while ( const auto count = reader.read() )
         {
            const auto frameCount = static_cast<std::uint64_t>( count );
            writeValue( frameCount );
            writeArray( rawIntensity, count );
            writeArray( rowIndex, count );
            writeArray( columnIndex, count );
            std::cout.flush();
            totalRead += frameCount;
         }
         reader.close();
      }
      else
      {
         std::vector<double> x( capacity );
         std::vector<double> y( capacity );
         std::vector<double> z( capacity );
         std::vector<float> processedIntensity( capacity );
         std::vector<e57::SourceDestBuffer> buffers;
         buffers.reserve( 7 );
         buffers.emplace_back( image, "cartesianX", x.data(), capacity );
         buffers.emplace_back( image, "cartesianY", y.data(), capacity );
         buffers.emplace_back( image, "cartesianZ", z.data(), capacity );
         buffers.emplace_back(
            image, "intensity", processedIntensity.data(), capacity );
         buffers.emplace_back(
            image, "easytry:rawIntensity", rawIntensity.data(), capacity );
         buffers.emplace_back( image, "rowIndex", rowIndex.data(), capacity );
         buffers.emplace_back( image, "columnIndex", columnIndex.data(), capacity );
         auto reader = points.reader( buffers );
         while ( const auto count = reader.read() )
         {
            const auto frameCount = static_cast<std::uint64_t>( count );
            writeValue( frameCount );
            writeArray( x, count );
            writeArray( y, count );
            writeArray( z, count );
            writeArray( processedIntensity, count );
            writeArray( rawIntensity, count );
            writeArray( rowIndex, count );
            writeArray( columnIndex, count );
            std::cout.flush();
            totalRead += frameCount;
         }
         reader.close();
      }

      const std::uint64_t endMarker = 0;
      writeValue( endMarker );
      std::cout.flush();
      image.close();

      if ( totalRead != header.pointCount )
      {
         throw std::runtime_error( "E57 point count changed during streaming." );
      }
      return EXIT_SUCCESS;
   }
}

#ifdef _WIN32
int wmain( const int argc, wchar_t *argv[] )
{
   try
   {
      if ( argc != 3 && argc != 4 )
      {
         throw std::invalid_argument(
            "Usage: EasyTryE57Reader.exe <input.e57> <buffer-capacity> [preview]" );
      }
      const auto capacityValue = std::wcstoull( argv[2], nullptr, 10 );
      if ( capacityValue == 0 ||
           capacityValue > std::numeric_limits<std::size_t>::max() )
      {
         throw std::invalid_argument( "Invalid buffer capacity." );
      }
      const bool previewOnly = argc == 4 && std::wstring( argv[3] ) == L"preview";
      if ( argc == 4 && !previewOnly )
      {
         throw std::invalid_argument( "Unknown E57 reader mode." );
      }
      return run(
         toUtf8( argv[1] ),
         static_cast<std::size_t>( capacityValue ),
         previewOnly );
   }
   catch ( const e57::E57Exception &error )
   {
      std::cerr << error.errorStr();
      if ( !error.context().empty() )
      {
         std::cerr << "; " << error.context();
      }
      std::cerr << std::endl;
      return EXIT_FAILURE;
   }
   catch ( const std::exception &error )
   {
      std::cerr << error.what() << std::endl;
      return EXIT_FAILURE;
   }
}
#else
int main( const int argc, char *argv[] )
{
   try
   {
      if ( argc != 3 && argc != 4 )
      {
         throw std::invalid_argument(
            "Usage: EasyTryE57Reader <input.e57> <buffer-capacity> [preview]" );
      }
      const bool previewOnly = argc == 4 && std::string( argv[3] ) == "preview";
      if ( argc == 4 && !previewOnly )
      {
         throw std::invalid_argument( "Unknown E57 reader mode." );
      }
      return run(
         argv[1],
         static_cast<std::size_t>( std::stoull( argv[2] ) ),
         previewOnly );
   }
   catch ( const std::exception &error )
   {
      std::cerr << error.what() << std::endl;
      return EXIT_FAILURE;
   }
}
#endif
